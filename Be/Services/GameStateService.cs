using Be.Contracts;
using Be.Data;
using Be.Domain;
using Be.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Be.Services;

public sealed class GameStateService(GameDbContext db, ScoreService scores, TimeProvider time)
{
    /// <summary>
    /// Snapshot for start-up and reconnects. <c>players</c> follows the same visibility rule as live events:
    /// <c>all</c> → everyone, <c>team</c> → the caller's team, <c>victim</c> → only the caller. The admin sees everyone.
    /// </summary>
    public async Task<GameStateSnapshot> GetAsync(CallerIdentity caller, CancellationToken ct = default)
    {
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == caller.SessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);
        var players = await db.Players.AsNoTracking()
            .Where(p => p.SessionId == session.Id)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
        var self = players.SingleOrDefault(p => p.Id == caller.PlayerId);

        IEnumerable<Player> visible = caller.IsAdmin
            ? players
            : session.Config.EliminationVisibility switch
            {
                EliminationVisibility.All => players,
                EliminationVisibility.Team => players.Where(p => p.TeamId == self?.TeamId),
                _ => players.Where(p => p.Id == self?.Id),
            };

        return new GameStateSnapshot
        {
            SessionId = session.Id,
            Status = session.Status,
            Config = session.Config.ToContract(),
            Self = self?.ToState(),
            Players = visible.Select(p => p.ToState()).ToList(),
            Score = await scores.GetAsync(session.Id, ct),
            MappingVersion = session.MappingVersion,
            StartedAt = session.StartedAt,
            EndsAt = session.EndsAt,
            ServerTime = time.UtcNow(),
        };
    }
}
