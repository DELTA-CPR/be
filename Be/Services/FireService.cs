using System.Text.Json;
using Be.Contracts;
using Be.Data;
using Be.Domain;
using Be.Infrastructure;
using Be.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Be.Services;

/// <summary>
/// Decides hits. One <c>Fire</c> call is one press of the fire button: the request is validated as a whole,
/// then each target independently. All state changes of one shot (target state, elimination rows and the stored
/// result) are committed in a single transaction, and events are sent only after the commit.
/// </summary>
/// <remarks>
/// Request-level check order:
/// <list type="number">
/// <item>Identity: well-formed shotId, token session = request session, session exists, caller is a linked player.</item>
/// <item>Idempotency: a known (sessionId, shooterId, shotId) returns the stored result with <c>duplicate: true</c>.
/// This runs before any game-state check, so a retry gets the original answer even if the shooter has been
/// eliminated or the match has ended since.</item>
/// <item>Match is <c>running</c> → otherwise <c>SESSION_NOT_ACTIVE</c>.</item>
/// <item>Shooter is <c>alive</c> or <c>protected</c> → otherwise <c>SHOOTER_NOT_ALIVE</c>.</item>
/// <item>1 to <c>maxTargetsPerShot</c> distinct targets → otherwise <c>INVALID_ARGUMENT</c>.</item>
/// <item>Rate limit → <c>RATE_LIMITED</c>; timestamp skew → <c>TIMESTAMP_OUT_OF_RANGE</c>.</item>
/// </list>
/// Per-target check order: exists in session, not the shooter, marker is not a spawn point, every reported marker
/// belongs to the target, team rule (friendly fire setting), target is <c>alive</c>. The alive → respawning
/// transition itself is a conditional UPDATE, so of two concurrent shots on one target only the one that actually
/// changed the row gets the elimination; the other gets <c>TARGET_NOT_ALIVE</c>.
/// </remarks>
public sealed class FireService(
    GameDbContext db,
    GameNotifier notifier,
    ScoreService scores,
    IOptions<FireRulesOptions> rules,
    TimeProvider time,
    ILogger<FireService> logger)
{
    private readonly FireRulesOptions _rules = rules.Value;

    private sealed record ProcessedShot(ShotResult Result, GameSession Session, Player Shooter, List<Elimination> Eliminations);

    public async Task<ShotResult> FireAsync(CallerIdentity caller, ShotRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ShotId) || request.ShotId.Length > _rules.MaxShotIdLength)
        {
            throw GameException.BadRequest(ErrorCodes.InvalidArgument, "shotId is required.");
        }

        if (request.SessionId != caller.SessionId)
        {
            throw GameException.Forbidden(ErrorCodes.SessionMismatch);
        }

        var shooterId = caller.RequireLinkedPlayer();
        var shotId = request.ShotId;

        // Fast path for retries; the unique key below covers concurrent duplicates.
        if (await FindStoredAsync(caller.SessionId, shooterId, shotId, cancellationToken) is { } stored)
        {
            return stored;
        }

        var processed = await ProcessAsync(caller, shooterId, request, cancellationToken);
        if (processed is null)
        {
            // Lost the race against a concurrent request with the same shotId; it has committed by now.
            return await FindStoredAsync(caller.SessionId, shooterId, shotId, cancellationToken)
                ?? throw new InvalidOperationException("Duplicate shot without a stored result.");
        }

        await NotifyAsync(processed, cancellationToken);
        return processed.Result;
    }

    private async Task<ProcessedShot?> ProcessAsync(CallerIdentity caller, string shooterId, ShotRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // FOR SHARE: concurrent shots proceed in parallel, but EndGame/timeout (an UPDATE of this row) waits for
        // in-flight shots to commit, and shots that arrive after the match closed see status 'Ended'. The final
        // score therefore contains exactly the eliminations committed before the match was closed.
        var session = (await db.Sessions
            .FromSql($"""SELECT * FROM "Sessions" WHERE "Id" = {caller.SessionId} FOR SHARE""")
            .AsNoTracking()
            .ToListAsync(ct)).SingleOrDefault()
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);

        var shooter = await db.Players.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == shooterId && p.SessionId == session.Id, ct);
        if (shooter is null || shooter.DeviceLinkId != caller.DeviceLinkId)
        {
            throw GameException.Forbidden(detail: "The caller is not linked to a registered player.");
        }

        var now = time.UtcNow();
        var record = new ShotRecord
        {
            SessionId = session.Id,
            ShooterId = shooterId,
            ShotId = request.ShotId!,
            ReceivedAt = now,
            ResultJson = "{}",
        };
        db.ShotRecords.Add(record);
        try
        {
            // Claims the shotId. A concurrent duplicate blocks on the primary key until we commit, then fails here.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return null;
        }

        var eliminations = new List<Elimination>();
        var rejection = await ValidateRequestAsync(session, shooter, request, now, ct);
        ShotResult result;
        if (rejection is not null)
        {
            logger.LogInformation(
                "Shot {ShotId} by {ShooterId} in {SessionId} rejected: {Reason}",
                request.ShotId, shooterId, session.Id, rejection);
            result = new ShotResult { ShotId = request.ShotId!, Accepted = false, ServerTime = now, ShotRejectedReason = rejection };
        }
        else
        {
            record.CountsForRateLimit = true;
            var targets = await EvaluateTargetsAsync(session, shooter, request.ShotId!, MergeTargets(request.Targets!), now, eliminations, ct);
            result = new ShotResult
            {
                ShotId = request.ShotId!,
                Accepted = eliminations.Count > 0,
                ServerTime = now,
                Targets = targets,
            };
        }

        db.Eliminations.AddRange(eliminations);
        record.ResultJson = JsonSerializer.Serialize(result, GameJson.Options);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ProcessedShot(result, session, shooter, eliminations);
    }

    private async Task<string?> ValidateRequestAsync(GameSession session, Player shooter, ShotRequest request, DateTime now, CancellationToken ct)
    {
        if (session.Status != SessionStatus.Running)
        {
            return ErrorCodes.SessionNotActive;
        }

        if (shooter.Status is not (PlayerStatus.Alive or PlayerStatus.Protected))
        {
            return ErrorCodes.ShooterNotAlive;
        }

        if (request.Targets is not { Count: > 0 } targets
            || targets.Any(t => t is null || string.IsNullOrWhiteSpace(t.PlayerId))
            || targets.Select(t => t.PlayerId).Distinct().Count() > session.Config.MaxTargetsPerShot)
        {
            return ErrorCodes.InvalidArgument;
        }

        if (_rules.MinShotIntervalMs > 0)
        {
            var since = now.AddMilliseconds(-_rules.MinShotIntervalMs);
            var recent = await db.ShotRecords.AsNoTracking().AnyAsync(
                r => r.SessionId == session.Id && r.ShooterId == shooter.Id && r.CountsForRateLimit
                    && r.ShotId != request.ShotId && r.ReceivedAt > since,
                ct);
            if (recent)
            {
                return ErrorCodes.RateLimited;
            }
        }

        if (request.Timestamp is not { } timestamp
            || Math.Abs((timestamp.ToUniversalTime() - now).TotalMilliseconds) > _rules.MaxClockSkewMs)
        {
            return ErrorCodes.TimestampOutOfRange;
        }

        return null;
    }

    /// <summary>Merges repeated entries of one player; sorted so that concurrent shots lock player rows in the same order.</summary>
    private static List<(string PlayerId, HashSet<int> MarkerIds)> MergeTargets(IEnumerable<ShotTarget> targets) =>
        targets
            .GroupBy(t => t.PlayerId, StringComparer.Ordinal)
            .Select(g => (g.Key, g.SelectMany(t => t.MarkerIds ?? []).ToHashSet()))
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .ToList();

    private async Task<List<TargetResult>> EvaluateTargetsAsync(
        GameSession session,
        Player shooter,
        string shotId,
        List<(string PlayerId, HashSet<int> MarkerIds)> targets,
        DateTime now,
        List<Elimination> eliminations,
        CancellationToken ct)
    {
        var ids = targets.Select(t => t.PlayerId).ToList();
        var players = await db.Players.AsNoTracking()
            .Where(p => p.SessionId == session.Id && ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        var spawnMarkers = (await db.SpawnPoints.AsNoTracking()
            .Where(s => s.SessionId == session.Id)
            .Select(s => s.MarkerId)
            .ToListAsync(ct)).ToHashSet();

        var config = session.Config;
        var respawnAt = now.AddSeconds(config.RespawnSeconds);
        var results = new List<TargetResult>(targets.Count);

        foreach (var (playerId, markerIds) in targets)
        {
            players.TryGetValue(playerId, out var target);
            var reason =
                target is null ? ErrorCodes.TargetNotFound
                : target.Id == shooter.Id ? ErrorCodes.TargetIsSelf
                : markerIds.Overlaps(spawnMarkers) ? ErrorCodes.MarkerIsSpawnPoint
                : markerIds.Count == 0 || markerIds.Any(m => m != target.MarkerId) ? ErrorCodes.MarkerMismatch
                : target.TeamId == shooter.TeamId && !config.FriendlyFire ? ErrorCodes.SameTeam
                : target.Status != PlayerStatus.Alive ? ErrorCodes.TargetNotAlive
                : null;

            if (reason is null)
            {
                // The database guards the alive → respawning transition. Only the shot that changes the row scores.
                var changed = await db.Players
                    .Where(p => p.Id == target!.Id && p.Status == PlayerStatus.Alive)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, PlayerStatus.Respawning)
                        .SetProperty(p => p.HitAt, now)
                        .SetProperty(p => p.RespawnAt, respawnAt)
                        .SetProperty(p => p.ProtectedUntil, (DateTime?)null), ct);
                reason = changed == 1 ? null : ErrorCodes.TargetNotAlive;
            }

            if (reason is not null)
            {
                logger.LogInformation(
                    "Shot target {TargetId} by {ShooterId} in {SessionId} rejected: {Reason}",
                    playerId, shooter.Id, session.Id, reason);
                results.Add(new TargetResult { PlayerId = playerId, Outcome = TargetOutcome.Rejected, Reason = reason });
                continue;
            }

            var friendlyFire = target!.TeamId == shooter.TeamId;
            eliminations.Add(new Elimination
            {
                SessionId = session.Id,
                ShotId = shotId,
                ShooterId = shooter.Id,
                ShooterTeamId = shooter.TeamId,
                TargetId = target.Id,
                TargetTeamId = target.TeamId,
                OccurredAt = now,
                RespawnAt = respawnAt,
                FriendlyFire = friendlyFire,
                // Rule for v1: a friendly-fire elimination is recorded but does not change the team score.
                CountsForScore = !friendlyFire,
            });
            results.Add(new TargetResult { PlayerId = playerId, Outcome = TargetOutcome.Eliminated, RespawnAt = respawnAt });
        }

        return results;
    }

    private async Task NotifyAsync(ProcessedShot shot, CancellationToken ct)
    {
        if (shot.Eliminations.Count == 0)
        {
            return;
        }

        var mode = shot.Session.Config.RespawnMode;
        foreach (var e in shot.Eliminations)
        {
            await notifier.PlayerHit(e.TargetId, new PlayerHitEvent
            {
                PlayerId = e.TargetId,
                Status = PlayerStatus.Respawning,
                HitAt = e.OccurredAt,
                RespawnAt = e.RespawnAt,
                RespawnMode = mode,
                ShotId = e.ShotId,
                ByPlayerId = e.ShooterId,
            });
            await notifier.PlayerEliminated(shot.Session, e.TargetId, e.TargetTeamId, new EliminationEvent
            {
                PlayerId = e.TargetId,
                TeamId = e.TargetTeamId,
                ByPlayerId = e.ShooterId,
                ByTeamId = e.ShooterTeamId,
                At = e.OccurredAt,
                RespawnAt = e.RespawnAt,
                FriendlyFire = e.FriendlyFire,
                ShotId = e.ShotId,
            }, ct);
        }

        // One scoreboard per shot, after all its targets are processed.
        if (shot.Eliminations.Any(e => e.CountsForScore))
        {
            await notifier.ScoreUpdated(shot.Session.Id, await scores.GetAsync(shot.Session.Id, ct));
        }
    }

    private async Task<ShotResult?> FindStoredAsync(string sessionId, string shooterId, string shotId, CancellationToken ct)
    {
        var json = await db.ShotRecords.AsNoTracking()
            .Where(r => r.SessionId == sessionId && r.ShooterId == shooterId && r.ShotId == shotId)
            .Select(r => r.ResultJson)
            .SingleOrDefaultAsync(ct);
        return json is null ? null : JsonSerializer.Deserialize<ShotResult>(json, GameJson.Options)! with { Duplicate = true };
    }
}
