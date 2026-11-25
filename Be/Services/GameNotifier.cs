using Be.Contracts;
using Be.Data;
using Be.Domain;
using Be.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Be.Services;

/// <summary>
/// Sends hub events. Callers invoke it only after the database transaction has committed, so every
/// message describes durable state that a reconnecting client can read back through <c>GetGameState</c>.
/// </summary>
public sealed class GameNotifier(IHubContext<GameHub, IGameClient> hub, GameDbContext db)
{
    public Task PlayerHit(string playerId, PlayerHitEvent e) => hub.Clients.User(playerId).PlayerHit(e);

    public async Task PlayerEliminated(GameSession session, string victimId, string victimTeamId, EliminationEvent e, CancellationToken ct) =>
        await (await VisibleTo(session, victimId, victimTeamId, ct)).PlayerEliminated(e);

    public async Task PlayerRespawned(GameSession session, string playerId, string teamId, PlayerRespawnedEvent e, CancellationToken ct) =>
        await (await VisibleTo(session, playerId, teamId, ct)).PlayerRespawned(e);

    public async Task PlayerStateChanged(GameSession session, string playerId, string teamId, PlayerStateEvent e, CancellationToken ct) =>
        await (await VisibleTo(session, playerId, teamId, ct)).PlayerStateChanged(e);

    public Task ScoreUpdated(string sessionId, Scoreboard score) => hub.Clients.Group(HubGroups.Session(sessionId)).ScoreUpdated(score);

    public Task RosterUpdated(string sessionId, MappingTable table) => hub.Clients.Group(HubGroups.Session(sessionId)).RosterUpdated(table);

    public Task GameConfigUpdated(string sessionId, GameConfig config) => hub.Clients.Group(HubGroups.Session(sessionId)).GameConfigUpdated(config);

    public Task GameStarted(string sessionId, GameStartedEvent e) => hub.Clients.Group(HubGroups.Session(sessionId)).GameStarted(e);

    public Task GameEnded(string sessionId, GameEndedEvent e) => hub.Clients.Group(HubGroups.Session(sessionId)).GameEnded(e);

    public Task Kicked(string playerId, string reason) => hub.Clients.User(playerId).Kicked(reason);

    /// <summary>
    /// Recipients of player-state events by <see cref="EliminationVisibility"/>. The session admin always sees
    /// everything, matching what <c>GetGameState</c> returns to the admin. Team recipients are resolved from the
    /// database at send time (not from hub groups), so a team change in the lobby can never leave a stale group.
    /// </summary>
    private async Task<IGameClient> VisibleTo(GameSession session, string playerId, string teamId, CancellationToken ct)
    {
        if (session.Config.EliminationVisibility == EliminationVisibility.All)
        {
            return hub.Clients.Group(HubGroups.Session(session.Id));
        }

        var users = new HashSet<string> { playerId, session.AdminSubject };
        if (session.AdminPlayerId is not null)
        {
            users.Add(session.AdminPlayerId);
        }

        if (session.Config.EliminationVisibility == EliminationVisibility.Team)
        {
            users.UnionWith(await db.Players.AsNoTracking()
                .Where(p => p.SessionId == session.Id && p.TeamId == teamId)
                .Select(p => p.Id)
                .ToListAsync(ct));
        }

        return hub.Clients.Users(users);
    }
}
