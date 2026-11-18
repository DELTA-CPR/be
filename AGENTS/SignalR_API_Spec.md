# Game of Tag – SignalR API Specification
## 1. Scope and conventions

This document defines the real-time API between the Unity mobile client and the ASP.NET Core backend. It covers connection and authentication, hub methods (client → server), client callbacks (server → client), data contracts, validation rules, error handling, reconnection and scaling.

Session creation and joining by QR code are assumed to be plain REST endpoints (Section 3.1) because they happen before a SignalR connection exists. Everything that happens during a game goes through SignalR.

**Conventions**

- Hub endpoint: `/hubs/game`
- Hub protocol: JSON (default). MessagePack may be enabled later without changing the contracts.
- Property names: `camelCase`. Enums: lowercase strings (`"alive"`, `"respawning"`).
- Timestamps: ISO 8601 UTC with `Z` suffix, for example `2026-11-18T10:30:00Z`.
- IDs: player, session and shot IDs are opaque strings. Marker IDs are integers.
- **The server is authoritative** for player state, respawn timing, configuration and rosters. The client reports detections; it never dictates outcomes.
- No camera frames or video are ever sent over this API.

---

## 2. Roles

| Role | Description |
| --- | --- |
| `admin` | Creates and manages the session. Also a normal player. |
| `player` | Registered participant linked to a device. |
| `pending` | Joined the session, but not yet linked to a registered player (before device linking, Section 5.2). |

Admin-only hub methods return `FORBIDDEN` for other roles.

---

## 3. Connection and authentication

### 3.1 REST bootstrap (outside SignalR)

| Endpoint | Purpose |
| --- | --- |
| `POST /api/sessions` | Admin creates a session. Returns `sessionId`, `sessionCode`, `joinQrPayload`, `adminToken`. |
| `POST /api/sessions/join` | Player submits `sessionCode` (from QR or typed). Returns a session-scoped `accessToken` with role `pending`. |

### 3.2 Token

The access token is a JWT containing:

| Claim | Meaning |
| --- | --- |
| `sub` | Connection identity (`playerId` once linked, otherwise a pending-user ID) |
| `sessionId` | Game session the token is valid for |
| `role` | `admin`, `player` or `pending` |

Browsers and many clients cannot set headers on WebSocket connections, so the Unity client passes the token as `?access_token=…` on the hub URL. The server reads it for `/hubs/game` only.

After a successful `LinkDevice` the server returns an upgraded token (role `player`, `sub = playerId`). The client must reconnect with it so the user identifier matches the player.

### 3.3 Server-side identity and groups

The shooter is **always derived from the authenticated connection**, never from a client field. In `OnConnectedAsync` the server adds the connection to:

| Group | Members | Used for |
| --- | --- | --- |
| `session:{sessionId}` | all connections | game-wide events |
| `team:{sessionId}:{teamId}` | players of that team | team-only events |
| `admins:{sessionId}` | admin connections | admin notifications |

Direct messages to one player use `Clients.User(playerId)`. Because groups are lost on reconnect, they are re-created from the token in `OnConnectedAsync`, not by the client.

### 3.4 Connection lifecycle

1. Client calls `POST /api/sessions/join` and gets a `pending` token.
2. Client connects to `/hubs/game` with the token.
3. Client calls `LinkDevice(markerId)`, then reconnects with the upgraded token.
4. Client calls `GetMappingTable()`, `GetGameConfig()` and `GetGameState()`.
5. Game runs; client calls `Fire(...)` and handles callbacks.
6. On start-up and after every reconnect, the client calls `GetGameState()` to resynchronize.

---

## 4. Typed hub contract (C#)

```csharp
public interface IGameClient
{
    Task GameConfigUpdated(GameConfig config);
    Task RosterUpdated(MappingTable table);
    Task GameStarted(GameStartedEvent e);
    Task GameEnded(GameEndedEvent e);
    Task PlayerHit(PlayerHitEvent e);             // to the eliminated player
    Task PlayerEliminated(EliminationEvent e);    // broadcast, scope per config
    Task PlayerRespawned(PlayerRespawnedEvent e);
    Task PlayerStateChanged(PlayerStateEvent e);
    Task Kicked(string reason);
}

public class GameHub : Hub<IGameClient>
{
    // Any authenticated user
    Task<LinkDeviceResult> LinkDevice(int markerId);
    Task<MappingTable>     GetMappingTable();
    Task<GameConfig>       GetGameConfig();
    Task<GameStateSnapshot> GetGameState();
    Task<ServerTime>       GetServerTime();

    // Players
    Task<ShotResult>       Fire(ShotRequest request);
    Task<ConfirmSpawnResult> ConfirmSpawn(int spawnMarkerId);   // optional, Section 8.3

    // Admin only
    Task<PlayerRegistration> RegisterPlayer(RegisterPlayerRequest request);
    Task                   UpdatePlayer(UpdatePlayerRequest request);
    Task                   RemovePlayer(string playerId);
    Task<SpawnPoint>       RegisterSpawnPoint(RegisterSpawnPointRequest request);
    Task                   RemoveSpawnPoint(string spawnPointId);
    Task                   UpdateGameConfig(GameConfig config);   // only before start
    Task                   StartGame();
    Task                   EndGame();
}
```

---

## 5. Client → server methods

### 5.1 Summary

| Method | Role | Returns | Purpose |
| --- | --- | --- | --- |
| `LinkDevice` | pending, player | `LinkDeviceResult` | Bind this device to a registered player by scanning one of their markers |
| `GetMappingTable` | any | `MappingTable` | Marker → player/spawn lookup, downloaded at game start |
| `GetGameConfig` | any | `GameConfig` | Current configuration |
| `GetGameState` | any | `GameStateSnapshot` | Full resync snapshot |
| `GetServerTime` | any | `ServerTime` | Clock offset estimation |
| `Fire` | player | `ShotResult` | Report a hit on one or more enemy players |
| `ConfirmSpawn` | player | `ConfirmSpawnResult` | Optional spawn point verification |
| `RegisterPlayer` | admin | `PlayerRegistration` | Create a player from two scanned markers |
| `UpdatePlayer` | admin | – | Change nickname or team |
| `RemovePlayer` | admin | – | Remove a player and free their markers |
| `RegisterSpawnPoint` | admin | `SpawnPoint` | Register a spawn marker |
| `RemoveSpawnPoint` | admin | – | Delete a spawn point |
| `UpdateGameConfig` | admin | – | Change configuration (lobby only) |
| `StartGame` | admin | – | Lobby → running |
| `EndGame` | admin | – | Running → ended |

### 5.2 `LinkDevice(markerId)`

Used after joining, when the player scans one of their own markers with the front camera.

Checks: the session exists, the marker is registered to a player in this session with role `chest` or `back`, and that player is not already linked to another active device.

Result:

```json
{
  "success": true,
  "playerId": "player_123",
  "nickname": "Player123",
  "teamId": "blue",
  "upgradedToken": "<jwt>"
}
```

Failure codes: `MARKER_NOT_REGISTERED`, `MARKER_IS_SPAWN_POINT`, `PLAYER_ALREADY_LINKED`, `SESSION_NOT_FOUND`.

> **Decision:** if a player is already linked, the call is rejected rather than silently replacing the existing device. An admin can release a link by calling `UpdatePlayer` with `releaseDevice: true`. This avoids a copied marker hijacking a player's session.

### 5.3 `GetMappingTable()`

Returns everything the client needs to identify players and spawn points locally, so that marker detections never need to be sent to the server.

```json
{
  "sessionId": "game_001",
  "version": 7,
  "markers": [
    { "markerId": 12, "kind": "player", "role": "chest", "playerId": "player_123", "nickname": "Player123", "teamId": "blue" },
    { "markerId": 13, "kind": "player", "role": "back",  "playerId": "player_123", "nickname": "Player123", "teamId": "blue" },
    { "markerId": 40, "kind": "spawn", "spawnPointId": "spawn_a", "name": "Blue base", "teamId": "blue" }
  ],
  "teams": [
    { "teamId": "blue", "name": "Blue Team", "color": "#1E66F5" },
    { "teamId": "red",  "name": "Red Team",  "color": "#D20F39" }
  ]
}
```

`kind: "spawn"` entries tell the client to exclude those markers from targeting. `version` increases on every change; the client keeps the highest version it has seen.

### 5.4 `GetGameState()`

Returns the full snapshot used at start-up and after reconnecting:

```json
{
  "sessionId": "game_001",
  "status": "running",
  "config": { },
  "self": { "playerId": "player_123", "status": "alive", "respawnAt": null },
  "players": [
    { "playerId": "player_456", "status": "respawning", "respawnAt": "2026-11-18T10:35:00Z" }
  ],
  "mappingVersion": 7,
  "serverTime": "2026-11-18T10:31:12Z"
}
```

Which entries appear in `players` is limited by the elimination visibility setting (Section 7.1).

### 5.5 `GetServerTime()`

Returns `{ "serverTime": "…" }`. The client measures round-trip time, estimates its clock offset, and uses it to render respawn countdowns correctly. The client's own clock is never used to decide whether a respawn has finished.

### 5.6 `Fire(ShotRequest)`

The core gameplay call. It is sent only when the client has found a valid enemy target (a shot with no valid target is a local miss and is **not** sent).

```json
{
  "sessionId": "game_001",
  "shotId": "shot_000123",
  "timestamp": "2026-11-18T10:30:00Z",
  "targets": [
    { "playerId": "player_456", "markerIds": [21] },
    { "playerId": "player_789", "markerIds": [31] }
  ]
}
```

> **Change from the proposal:** the proposal sends `targetPlayerIds` and `targetMarkerIds` as two flat arrays. Pairing them in `targets` removes the ambiguity about which marker belongs to which player, which matters in the multi-player bounding box case. A player seen through both markers lists both IDs.

Field rules:

| Field | Rule |
| --- | --- |
| `sessionId` | Must match the token's session; otherwise `SESSION_MISMATCH`. |
| `shotId` | Client-generated, unique per shooter per session (UUID recommended). Used for idempotency. |
| `timestamp` | Client's estimate of when the fire button was pressed, adjusted by the clock offset. Informational and plausibility-checked only. |
| `targets` | 1 to `maxTargetsPerShot` entries (default 8). Duplicate `playerId` entries are merged. |

The shooter is not part of the request; it comes from the connection.

**Result:**

```json
{
  "shotId": "shot_000123",
  "accepted": true,
  "duplicate": false,
  "serverTime": "2026-11-18T10:30:00.120Z",
  "targets": [
    { "playerId": "player_456", "outcome": "eliminated", "respawnAt": "2026-11-18T10:35:00Z" },
    { "playerId": "player_789", "outcome": "rejected", "reason": "TARGET_NOT_ALIVE" }
  ]
}
```

- `accepted` is `true` if at least one target was eliminated.
- Each target is validated independently; one rejected target never blocks the others.
- `outcome` is `eliminated` or `rejected`.
- A repeated `shotId` from the same shooter changes no state and returns the **original stored result** with `duplicate: true`. This makes retries after a network drop safe.
- If the whole request is invalid (shooter dead, session inactive, malformed), `accepted` is `false` and `shotRejectedReason` is set at the top level instead of per-target reasons.

### 5.7 `ConfirmSpawn(spawnMarkerId)`

Optional, enabled by `requireSpawnConfirmation` in the configuration (Section 8.3). The client sends the ID of the spawn marker it has just detected. Result: `{ "confirmed": true, "state": "alive" }` or a failure code (`NOT_A_SPAWN_MARKER`, `WRONG_TEAM_SPAWN`, `RESPAWN_NOT_READY`).

### 5.8 Admin methods

**`RegisterPlayer`**

```json
{ "nickname": "Player123", "teamId": "blue", "chestMarkerId": 12, "backMarkerId": 13 }
```

Returns `PlayerRegistration { playerId, nickname, teamId, chestMarkerId, backMarkerId }`. Validation: both markers are inside the dictionary range, are not already used by a player or spawn point, and are different from each other; nickname is unique within the session. Allowed in lobby only. After success the server broadcasts `RosterUpdated`.

**`RegisterSpawnPoint`**

```json
{ "markerId": 40, "name": "Blue base", "teamId": "blue" }
```

`teamId` is optional (neutral spawn). Returns `SpawnPoint { spawnPointId, markerId, name, teamId }`.

**`UpdateGameConfig(config)`** is accepted only while the session is in `lobby`; the server validates ranges and broadcasts `GameConfigUpdated`.

**`StartGame()`** requires at least two teams with at least one registered player each. It sets the session to `running`, freezes the configuration and broadcasts `GameStarted`.

**`EndGame()`** sets the session to `ended`, cancels pending respawn timers and broadcasts `GameEnded`.

---

## 6. Server → client callbacks

| Callback | Sent to | Trigger |
| --- | --- | --- |
| `GameConfigUpdated` | `session` | Admin changes config in lobby |
| `RosterUpdated` | `session` | Player, team or spawn point registered, changed or removed |
| `GameStarted` | `session` | `StartGame` |
| `GameEnded` | `session` | `EndGame` or match duration expiry |
| `PlayerHit` | the eliminated player | Valid hit accepted |
| `PlayerEliminated` | per visibility setting | Valid hit accepted |
| `PlayerRespawned` | per visibility setting, plus the player | Respawn timer expired (or confirmed) |
| `PlayerStateChanged` | per visibility setting | Any other state change (for example `protected`) |
| `Kicked` | one connection | Player removed or device replaced |

### 6.1 Payloads

**`PlayerHit`** (private to the eliminated player; matches the proposal)

```json
{
  "event": "PlayerHit",
  "playerId": "player_456",
  "status": "respawning",
  "hitAt": "2026-11-18T10:30:00Z",
  "respawnAt": "2026-11-18T10:35:00Z",
  "shotId": "shot_000123"
}
```

`hitAt` is the **server's** time of processing. The client renders a countdown from `respawnAt` using its estimated clock offset.

**`PlayerEliminated`**

```json
{ "playerId": "player_456", "teamId": "red", "byPlayerId": "player_123", "at": "2026-11-18T10:30:00Z", "respawnAt": "2026-11-18T10:35:00Z" }
```

`byPlayerId` is omitted when the visibility setting hides the shooter.

**`PlayerRespawned`**

```json
{ "playerId": "player_456", "status": "alive", "at": "2026-11-18T10:35:00Z" }
```

**`PlayerStateChanged`**

```json
{ "playerId": "player_456", "status": "protected", "until": "2026-11-18T10:35:05Z" }
```

**`GameStarted`** `{ "startedAt": "…", "endsAt": "…" | null }`

**`GameEnded`** `{ "endedAt": "…", "reason": "admin" | "timeout", "scores": [ { "teamId": "blue", "eliminations": 14 } ] }`

### 6.2 Ordering

When a hit is accepted, the server writes to the database first, then sends `PlayerHit`, then `PlayerEliminated`, then returns the `Fire` result. The result is returned only after the state is durable, so a client that sees `accepted: true` can rely on it.

---

## 7. Data contracts

### 7.1 `GameConfig`

```json
{
  "crosshairSize": 0.12,
  "fireWindowMs": 150,
  "respawnSeconds": 300,
  "matchDurationSeconds": 1800,
  "maxTargetsPerShot": 8,
  "requireSpawnConfirmation": false,
  "spawnProtectionSeconds": 0,
  "eliminationVisibility": "all"
}
```

| Field | Meaning |
| --- | --- |
| `crosshairSize` | Crosshair size, relative to the shorter screen dimension, so it is device independent. Identical for all players. |
| `fireWindowMs` | The `N` value: how long a fire input stays valid for hit detection. |
| `respawnSeconds` | Server-side respawn delay. |
| `matchDurationSeconds` | Optional; `null` for no limit. |
| `maxTargetsPerShot` | Upper bound for `targets` in one `Fire`. |
| `requireSpawnConfirmation` | If true, the player must call `ConfirmSpawn` after the timer expires (Section 8.3). |
| `spawnProtectionSeconds` | Duration of the `protected` state after respawning (0 disables it). |
| `eliminationVisibility` | `all`, `team` (victim's team only) or `victim` (only the eliminated player). Controls who receives `PlayerEliminated` and `PlayerRespawned`. |

The proposal says crosshair size is configurable but does not define its unit. A normalized unit is proposed here so that all devices apply the same setting; confirm this against the client implementation.

### 7.2 Enums

| Enum | Values |
| --- | --- |
| `PlayerStatus` | `alive`, `respawning`, `protected` |
| `SessionStatus` | `lobby`, `running`, `ended` |
| `MarkerKind` | `player`, `spawn` |
| `MarkerRole` | `chest`, `back` |
| `TargetOutcome` | `eliminated`, `rejected` |

### 7.3 Other types

`ServerTime` is `{ "serverTime": "…" }`. `PlayerRegistration`, `SpawnPoint`, `LinkDeviceResult` and `ConfirmSpawnResult` are shown inline in Section 5.

---

## 8. Validation and rules

### 8.1 Validation order for `Fire`

The server applies these checks in order and stops at the first request-level failure:

1. Request is well-formed (`shotId` present, 1 to `maxTargetsPerShot` targets, no unknown IDs).
2. Session exists, token session matches, and status is `running`.
3. Shooter is registered in the session and is `alive` or `protected`.
4. Idempotency: `shotId` not seen before for this shooter. If seen, return the stored result with `duplicate: true`.
5. Rate limit not exceeded (see 8.2).
6. Timestamp plausibility (see 8.2).

Then, **for each target independently**:

7. Player exists in the session.
8. Target is not the shooter.
9. Target is on a different team (friendly fire disabled).
10. Every reported marker ID is registered to that player.
11. Target is `alive` (a `protected` or `respawning` target is rejected).
12. Target has not already been eliminated by another shot in the same transaction.

Targets that pass are eliminated in **one database transaction** with a row-level lock on each player's row, so two shooters hitting the same target at the same moment produce exactly one elimination (the first to commit wins; the other gets `TARGET_NOT_ALIVE`).

### 8.2 Anti-abuse measures

The server cannot verify camera footage, so a hit is a client-reported event. These measures limit the damage from a modified client:

- **Rate limit:** a minimum interval between accepted `Fire` calls per shooter (default 250 ms). Excess calls return `RATE_LIMITED`.
- **Timestamp plausibility:** if the client timestamp differs from the server's receive time by more than `maxClockSkewMs` (default 2000 ms, after offset correction), the shot is rejected with `TIMESTAMP_OUT_OF_RANGE`.
- **Marker ownership:** marker IDs are checked against the registered owner, which blocks guessed or copied IDs that were never registered. It does not stop someone who copies a real marker.
- **Anomaly logging:** log every rejected shot with the reason so that the administrator can spot suspicious patterns (for example, very high accuracy at long range).

### 8.3 Respawn rules

- A hit sets the player to `respawning` and stores `hitAt` and `respawnAt = hitAt + respawnSeconds`, using the server clock.
- While `respawning`, `Fire` returns `SHOOTER_NOT_ALIVE` and the player cannot be hit.
- When `respawnAt` is reached the server sets the player to `alive` (or `protected` if `spawnProtectionSeconds > 0`) and sends `PlayerRespawned`.
- If `requireSpawnConfirmation` is true, the status stays `respawning` after the timer expires until a valid `ConfirmSpawn` arrives. The proposal leaves this requirement open (Section 10.3); it is modeled here as an off-by-default option.

### 8.4 Respawn timer implementation

Timers must survive restarts and multiple server instances, so `respawnAt` is stored in PostgreSQL and a hosted service sweeps for expired respawns (for example every 250 to 500 ms), rather than relying on in-memory `Task.Delay`. Each sweep uses `UPDATE … WHERE status = 'respawning' AND respawn_at <= now() RETURNING …` so that only one instance handles each respawn.

---

## 9. Errors

Two kinds of failure exist:

1. **Protocol or authorization errors** throw a `HubException` with a stable code as its message. Examples: not authenticated, wrong role, malformed arguments.
2. **Business outcomes** (for example, a rejected hit) are returned in the result object and never thrown.

| Code | Where | Meaning |
| --- | --- | --- |
| `UNAUTHENTICATED` | any | Missing or expired token |
| `FORBIDDEN` | admin methods | Caller is not an admin |
| `INVALID_ARGUMENT` | any | Malformed or out-of-range input |
| `SESSION_NOT_FOUND` | any | Unknown session |
| `SESSION_MISMATCH` | `Fire` | Request session differs from token session |
| `SESSION_NOT_ACTIVE` | `Fire` | Session not `running` |
| `WRONG_PHASE` | admin | Method not allowed in the current session status |
| `SHOOTER_NOT_ALIVE` | `Fire` | Shooter is respawning |
| `RATE_LIMITED` | `Fire` | Too many shots |
| `TIMESTAMP_OUT_OF_RANGE` | `Fire` | Implausible client timestamp |
| `TARGET_NOT_FOUND` | `Fire` (per target) | Player not in session |
| `TARGET_IS_SELF` | `Fire` (per target) | Shooter listed as target |
| `SAME_TEAM` | `Fire` (per target) | Friendly fire |
| `MARKER_MISMATCH` | `Fire` (per target) | Marker not registered to that player |
| `TARGET_NOT_ALIVE` | `Fire` (per target) | Already eliminated, respawning or protected |
| `MARKER_NOT_REGISTERED` | `LinkDevice` | Unknown marker |
| `MARKER_IS_SPAWN_POINT` | `LinkDevice` | Marker belongs to a spawn point |
| `PLAYER_ALREADY_LINKED` | `LinkDevice` | Player already has a device |
| `MARKER_IN_USE` | `RegisterPlayer`, `RegisterSpawnPoint` | Marker already registered |
| `NICKNAME_TAKEN` | `RegisterPlayer`, `UpdatePlayer` | Duplicate nickname |
| `NOT_A_SPAWN_MARKER` | `ConfirmSpawn` | Marker is not a spawn point |
| `WRONG_TEAM_SPAWN` | `ConfirmSpawn` | Spawn point assigned to another team |
| `RESPAWN_NOT_READY` | `ConfirmSpawn` | Respawn timer has not expired |

---

## 10. Reconnection and offline behavior

- The client uses automatic reconnect with backoff (for example 0, 1, 2, 5, 10 seconds, then every 10 seconds).
- A reconnect produces a **new connection ID** and loses group membership. The server re-adds groups from the token in `OnConnectedAsync`; the client does not need to re-join groups.
- Messages sent while disconnected are **not buffered**. After every reconnect the client must call `GetGameState()` and compare `mappingVersion` with its local version, calling `GetMappingTable()` if the version has changed.
- A `Fire` that failed because of a dropped connection may be retried with the **same `shotId`**. The server deduplicates, so there is no double elimination.
- Because `fireWindowMs` is short (hundreds of milliseconds), a shot taken while offline is not queued; the client shows a connection warning and does not fire locally.
- Players who stay disconnected keep their state; a player who is `respawning` still respawns on schedule.
- Optionally, the server can send `Kicked` and close the connection when a second device attempts to take over a linked player (see `LinkDevice`).

---

## 11. Scaling and deployment

- All hub instances must use a shared backplane. Options are Redis or Azure SignalR Service. Without one, group and user messages do not reach clients connected to other instances.
- If clients use WebSockets only (`skipNegotiation: true` with `transport: WebSockets`), sticky sessions are unnecessary. Otherwise, enable them for negotiation.
- Game state lives in PostgreSQL, never in hub instance memory. Hubs are transient and must be stateless.
- The idempotency store (`shotId` → result) is a table keyed by `(sessionId, shooterId, shotId)`, with a TTL equal to the session length.
- Recommended server settings: keep-alive 15 s, client timeout 30 s, maximum message size 32 KB (the largest message is the mapping table).

---

## 12. Example sequence: a successful shot

1. The shooter's client detects an enemy bounding box with markers 21 and 31 under the crosshair while the fire press is inside `fireWindowMs`.
2. Client calls `Fire` with `shotId = shot_000123` and the `targets` array.
3. Server validates the request and each target, then eliminates the valid targets in one transaction.
4. Server sends `PlayerHit` to each eliminated player and `PlayerEliminated` according to `eliminationVisibility`.
5. Server returns the `ShotResult` to the shooter, who shows a hit marker.
6. After `respawnSeconds` the sweeper sets each eliminated player to `alive` and the server sends `PlayerRespawned`.
