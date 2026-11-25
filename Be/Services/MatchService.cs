using Be.Contracts;
using Be.Data;
using Be.Domain;
using Microsoft.EntityFrameworkCore;

namespace Be.Services;

/// <summary>Match lifecycle: lobby → running → ended.</summary>
public sealed class MatchService(GameDbContext db, GameNotifier notifier, ScoreService scores, TimeProvider time)
{
    public async Task StartAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);
        if (session.Status != SessionStatus.Lobby)
        {
            throw GameException.Conflict(ErrorCodes.WrongPhase);
        }

        var teamsWithPlayers = await db.Players.AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .Select(p => p.TeamId)
            .Distinct()
            .CountAsync(ct);
        if (teamsWithPlayers < 2)
        {
            throw GameException.Conflict(ErrorCodes.NotEnoughTeams, "At least two teams need at least one player each.");
        }

        var now = time.UtcNow();
        DateTime? endsAt = session.Config.MatchDurationSeconds is { } d ? now.AddSeconds(d) : null;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Sessions
            .Where(s => s.Id == sessionId && s.Status == SessionStatus.Lobby)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SessionStatus.Running)
                .SetProperty(x => x.StartedAt, now)
                .SetProperty(x => x.EndsAt, endsAt), ct);
        if (changed == 0)
        {
            throw GameException.Conflict(ErrorCodes.WrongPhase);
        }

        await db.Players
            .Where(p => p.SessionId == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PlayerStatus.Alive)
                .SetProperty(p => p.HitAt, (DateTime?)null)
                .SetProperty(p => p.RespawnAt, (DateTime?)null)
                .SetProperty(p => p.ProtectedUntil, (DateTime?)null), ct);
        await tx.CommitAsync(ct);

        await notifier.GameStarted(sessionId, new GameStartedEvent(now, endsAt, session.Config.ToContract()));
    }

    /// <summary>
    /// Ends a running match (admin or timeout) and broadcasts <c>GameEnded</c> with the final table. The UPDATE waits
    /// for in-flight <c>Fire</c> transactions (they hold a share lock on the session row), so the final table contains
    /// exactly the eliminations committed before the match closed; later shots get <c>SESSION_NOT_ACTIVE</c>.
    /// </summary>
    /// <returns>False if the match was not running (for example already ended by the other path).</returns>
    public async Task<bool> TryEndAsync(string sessionId, GameEndReason reason, CancellationToken ct = default)
    {
        var now = time.UtcNow();
        var changed = await db.Sessions
            .Where(s => s.Id == sessionId && s.Status == SessionStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SessionStatus.Ended)
                .SetProperty(x => x.EndedAt, now)
                .SetProperty(x => x.EndReason, reason), ct);
        if (changed == 0)
        {
            return false;
        }

        await notifier.GameEnded(sessionId, new GameEndedEvent(now, reason, await scores.GetAsync(sessionId, ct)));
        return true;
    }
}
