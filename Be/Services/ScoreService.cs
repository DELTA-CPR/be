using Be.Contracts;
using Be.Data;
using Microsoft.EntityFrameworkCore;

namespace Be.Services;

/// <summary>
/// Team score = number of confirmed eliminations of opponents, derived from the <see cref="Elimination"/> rows.
/// The version is the total number of scored eliminations, so it only grows and any client can tell which
/// scoreboard is newer without extra counters (and without lock contention between concurrent shots).
/// </summary>
public sealed class ScoreService(GameDbContext db)
{
    public async Task<Scoreboard> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var teamIds = await db.Teams.AsNoTracking()
            .Where(t => t.SessionId == sessionId)
            .OrderBy(t => t.TeamId)
            .Select(t => t.TeamId)
            .ToListAsync(cancellationToken);

        var counts = await db.Eliminations.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.CountsForScore)
            .GroupBy(e => e.ShooterTeamId)
            .Select(g => new { TeamId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TeamId, x => x.Count, cancellationToken);

        var teams = teamIds.Select(t => new TeamScore(t, counts.GetValueOrDefault(t))).ToList();
        return new Scoreboard(sessionId, counts.Values.Sum(), teams);
    }
}
