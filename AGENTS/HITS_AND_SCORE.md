# Hits and Score – Implementation Notes and Team Hand-off

Implements the task in `task/Zadani-pro-Kasku.docx` on top of [SignalR_API_Spec.md](SignalR_API_Spec.md), with the changes from [AI.txt](AI.txt): **one marker per player**, admin-selectable **respawn mode** (`timer` / `qr`) and admin-toggled **friendly fire**.

## Running

```bash
# PostgreSQL for local development
docker run -d --name be-pg -e POSTGRES_PASSWORD=<pick-one> -p 5432:5432 postgres:17-alpine

cd be
dotnet user-secrets --project Be set ConnectionStrings:GameDb "Host=localhost;Database=be;Username=postgres;Password=<pick-one>"
dotnet user-secrets --project Be set Jwt:SigningKey "<at least 32 random characters>"
dotnet run --project Be/Be.csproj      # applies EF migrations on start-up

dotnet test                             # needs Docker (Testcontainers starts PostgreSQL)
# or point the tests at an existing server: TEST_DB_CONNECTION="Host=...;Username=...;Password=..." dotnet test
```

## Changes to the API spec

| Area | Change |
| --- | --- |
| Markers | `RegisterPlayer { nickname, teamId, markerId }` – a single `markerId`. Mapping entries have no `role`. |
| `GameConfig` | Adds `friendlyFire` (bool, default `false`) and `respawnMode` (`timer` \| `qr`, default `timer`). `respawnMode: "qr"` replaces `requireSpawnConfirmation`. |
| `Fire` check order | Idempotency is checked right after identity/session, **before** match status and shooter state, so a retry gets the original result even after the shooter was eliminated or the match ended. |
| New per-target reason | `MARKER_IS_SPAWN_POINT`: a spawn marker is never evidence of a player hit. |
| New callback `ScoreUpdated` | `Scoreboard { sessionId, version, teams: [{ teamId, eliminations }] }`, sent once per shot that changed the score. |
| `GetGameState` | Adds `score` (same `Scoreboard`), `startedAt`, `endsAt`. |
| `GameEnded` | `{ endedAt, reason: "admin" \| "timeout", score: Scoreboard }`. |
| `PlayerHit` | Adds `respawnMode` and `byPlayerId`. |

## `Fire` – example and validation

Request (shooter comes from the token, never from the request):

```json
{
  "sessionId": "game_001",
  "shotId": "6f1c2a8e-...",
  "timestamp": "2026-11-18T10:30:00.000Z",
  "targets": [
    { "playerId": "player_cyril", "markerIds": [11] },
    { "playerId": "player_dana",  "markerIds": [12] }
  ]
}
```

Mixed result (blue were losing 3 : 4; Cyril was alive, Dana was already respawning → 4 : 4):

```json
{
  "shotId": "6f1c2a8e-...",
  "accepted": true,
  "duplicate": false,
  "serverTime": "2026-11-18T10:30:00.120Z",
  "targets": [
    { "playerId": "player_cyril", "outcome": "eliminated", "respawnAt": "2026-11-18T10:35:00.120Z" },
    { "playerId": "player_dana",  "outcome": "rejected",   "reason": "TARGET_NOT_ALIVE" }
  ]
}
```

Sending the same request again returns the same body with `"duplicate": true`. The score stays 4 : 4.

Whole-request rejection: `{ "shotId": "...", "accepted": false, "shotRejectedReason": "SESSION_NOT_ACTIVE", "targets": [] }`.

For the FE: a confirmed hit is `outcome: "eliminated"`, a rejected target is `outcome: "rejected"` plus `reason`, and "waiting" means the `Fire` invocation has not returned yet. If the connection drops, retry with the **same** `shotId`.

**Request-level checks (in order).** Identity and session errors are thrown as `HubException` with the code as its message. All later rejections come back in the result:

1. `shotId` present (`INVALID_ARGUMENT`), token session = request session (`SESSION_MISMATCH`), caller is a linked player (`FORBIDDEN`).
2. Known `(sessionId, shooterId, shotId)` → stored result, `duplicate: true`.
3. Match is `running` → `SESSION_NOT_ACTIVE`.
4. Shooter is `alive` or `protected` → `SHOOTER_NOT_ALIVE`.
5. 1 to `maxTargetsPerShot` distinct targets → `INVALID_ARGUMENT`.
6. Rate limit → `RATE_LIMITED`. Clock skew → `TIMESTAMP_OUT_OF_RANGE`.

**Per-target checks (in order).** Repeated entries for the same player are merged first.

`TARGET_NOT_FOUND` → `TARGET_IS_SELF` → `MARKER_IS_SPAWN_POINT` → `MARKER_MISMATCH` → `SAME_TEAM` (only when friendly fire is off) → `TARGET_NOT_ALIVE`.

The server's limits live in a single place: the `FireRules` section of `appsettings.json` (`MinShotIntervalMs` 250, `MaxClockSkewMs` 2000, `MaxTargetsPerShotLimit` 16). The per-match `maxTargetsPerShot` defaults to 8.

## Transaction and concurrency guarantees

All writes for one shot happen in **one PostgreSQL transaction**: the target's state, the `Eliminations` rows and the stored `ShotRecords` result. If any write fails, the whole transaction rolls back.

- **Duplicate delivery.** `ShotRecords` has the primary key `(SessionId, ShooterId, ShotId)`. The first request claims the key. A concurrent duplicate blocks on the key, gets a unique violation once the first commits, rolls back, and returns the stored result.
- **Two shooters, one target.** The `alive → respawning` change is a conditional `UPDATE … WHERE Status = 'Alive'`. Only the shot that actually changed the row records an elimination and scores. The other shot gets `TARGET_NOT_ALIVE`.
- **Score.** Each team's score is the count of `Eliminations` rows with `CountsForScore`, grouped by the shooter's team. Every row stores the match, shot, shooter, target, both teams and the server time, so any point can be explained afterwards. `version` is the total number of scored eliminations, so it only ever increases.
- **EndGame vs. last hit.** `Fire` holds `FOR SHARE` on the session row. `EndGame` and the match timeout are an `UPDATE` of that row, so they wait for in-flight shots to commit. Shots that arrive after the close get `SESSION_NOT_ACTIVE`. Rule: **whichever takes the session lock first wins.** `GameEnded` therefore contains exactly the eliminations committed before the match closed.
- **Simultaneous trade.** If A and B shoot each other at the same moment, both hits can be accepted. The shooter row is not locked, which avoids deadlocks.
- **Events go out after commit.** The order is `PlayerHit` → `PlayerEliminated` → `ScoreUpdated`, and then the shooter receives the `ShotResult`.

## Friendly-fire scoring rule (v1, to be confirmed with Adam and Matyáš)

With `friendlyFire: true` a teammate hit **eliminates** the target. It is recorded with `FriendlyFire = true` and `CountsForScore = false`, so the **team score does not change** and no `ScoreUpdated` is sent. This is covered by the test `Teammate_hit_eliminates_but_does_not_change_score_when_friendly_fire_is_on`.

## Who receives what

| Event | Recipients |
| --- | --- |
| `PlayerHit` | only the eliminated player (`Clients.User(playerId)`) |
| `PlayerEliminated`, `PlayerRespawned`, `PlayerStateChanged` | `eliminationVisibility`: `all` → whole session. `team` → the victim's team + admin. `victim` → the victim + admin |
| `ScoreUpdated` | whole session. It contains only team totals. *Open point: should the live score be hidden under `team`/`victim` visibility?* |
| `GameStarted`, `GameEnded`, `RosterUpdated`, `GameConfigUpdated` | whole session |
| `Kicked` | the removed or released player |
| `ShotResult` | the shooter (return value of `Fire`) |

`GetGameState().players` follows the same visibility rule: `all` → everyone, `team` → the caller's team, `victim` → only the caller, admin → everyone. A client that reconnects sees exactly what a client in the same role that stayed connected sees.

## Respawn (interface with Stas)

- **Hit:** status becomes `respawning`, with `hitAt = now` and `respawnAt = now + respawnSeconds` (server clock, millisecond precision).
- **`timer` mode:** the `RespawnSweeper` hosted service (every 250 ms, conditional UPDATEs, safe on multiple instances) sets the player `alive`, or `protected` when `spawnProtectionSeconds > 0`. It then sends `PlayerRespawned`, plus `PlayerStateChanged { status: "protected", until }`. When the protection ends it sends `PlayerStateChanged { status: "alive" }`.
- **`qr` mode:** `PlayerHit.respawnAt` is the earliest moment the player can call `ConfirmSpawn(spawnMarkerId)` with their own team's (or a neutral) spawn marker. Failure reasons: `NOT_A_SPAWN_MARKER`, `WRONG_TEAM_SPAWN`, `RESPAWN_NOT_READY`.
- **Fire button:** unlock it on `PlayerRespawned` (a `protected` player may shoot but cannot be hit).
