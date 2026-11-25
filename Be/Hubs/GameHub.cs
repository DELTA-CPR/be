using Be.Contracts;
using Be.Data;
using Be.Domain;
using Be.Infrastructure;
using Be.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Be.Hubs;

/// <summary>
/// The connected lobby and match. Stateless: all game state lives in PostgreSQL. The caller's identity
/// (including the shooter of <c>Fire</c>) always comes from the authenticated connection, never from arguments.
/// </summary>
[Authorize(Policy = GameHub.ConnectedPolicy)]
public sealed class GameHub(
    GameDbContext db,
    FireService fire,
    LobbyService lobby,
    MatchService match,
    RespawnService respawn,
    GameStateService state,
    TimeProvider time) : Hub<IGameClient>
{
    public const string Path = "/hubs/game";
    public const string ConnectedPolicy = "Connected";

    private CallerIdentity Caller => CallerIdentity.From(Context.User);

    private CancellationToken Aborted => Context.ConnectionAborted;

    public override async Task OnConnectedAsync()
    {
        var caller = Caller;
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == caller.SessionId, Aborted)
            ?? throw new HubException(ErrorCodes.SessionNotFound);

        if (caller.PlayerId is not null)
        {
            // A released or removed device link invalidates its token.
            var linked = await db.Players.AsNoTracking()
                .AnyAsync(p => p.Id == caller.PlayerId && p.SessionId == session.Id && p.DeviceLinkId == caller.DeviceLinkId, Aborted);
            if (!linked)
            {
                throw new HubException(ErrorCodes.Unauthenticated);
            }
        }

        // Groups are lost on reconnect, so they are always rebuilt from the token here.
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Session(session.Id), Aborted);
        if (caller.IsAdmin)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Admins(session.Id), Aborted);
        }

        await base.OnConnectedAsync();
    }

    // ---------- Connected admin or player ----------

    public Task<MappingTable> GetMappingTable() => lobby.GetMappingTableAsync(Caller.SessionId, Aborted);

    public Task<GameStateSnapshot> GetGameState() => state.GetAsync(Caller, Aborted);

    public ServerTimeResult GetServerTime() => new(time.UtcNow());

    // ---------- Linked players (including a linked admin) ----------

    public Task<ShotResult> Fire(ShotRequest request) => fire.FireAsync(Caller, request, Aborted);

    public Task<ConfirmSpawnResult> ConfirmSpawn(int spawnMarkerId) => respawn.ConfirmSpawnAsync(Caller, spawnMarkerId, Aborted);

    // ---------- Admin only ----------

    public Task<PlayerRegistration> RegisterPlayer(RegisterPlayerRequest request) =>
        lobby.RegisterPlayerAsync(Admin().SessionId, request, Aborted);

    public Task UpdatePlayer(UpdatePlayerRequest request) => lobby.UpdatePlayerAsync(Admin().SessionId, request, Aborted);

    public Task RemovePlayer(string playerId) => lobby.RemovePlayerAsync(Admin().SessionId, playerId, Aborted);

    public Task<SpawnPointInfo> RegisterSpawnPoint(RegisterSpawnPointRequest request) =>
        lobby.RegisterSpawnPointAsync(Admin().SessionId, request, Aborted);

    public Task RemoveSpawnPoint(string spawnPointId) => lobby.RemoveSpawnPointAsync(Admin().SessionId, spawnPointId, Aborted);

    public Task UpdateGameConfig(GameConfig config) => lobby.UpdateGameConfigAsync(Admin().SessionId, config, Aborted);

    public Task StartGame() => match.StartAsync(Admin().SessionId, Aborted);

    public async Task EndGame()
    {
        if (!await match.TryEndAsync(Admin().SessionId, GameEndReason.Admin, Aborted))
        {
            throw GameException.Conflict(ErrorCodes.WrongPhase);
        }
    }

    private CallerIdentity Admin()
    {
        var caller = Caller;
        caller.RequireAdmin();
        return caller;
    }
}
