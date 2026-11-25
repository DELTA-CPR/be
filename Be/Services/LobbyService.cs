using Be.Contracts;
using Be.Data;
using Be.Domain;
using Be.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Be.Services;

/// <summary>Admin roster, spawn point and configuration management. Changes broadcast the new state after saving.</summary>
public sealed class LobbyService(
    GameDbContext db,
    GameNotifier notifier,
    IOptions<MarkerOptions> markers,
    IOptions<FireRulesOptions> fireRules)
{
    public async Task<MappingTable> GetMappingTableAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await db.Sessions.AsNoTracking().Include(s => s.Teams).SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);
        var players = await db.Players.AsNoTracking().Where(p => p.SessionId == sessionId).OrderBy(p => p.MarkerId).ToListAsync(ct);
        var spawns = await db.SpawnPoints.AsNoTracking().Where(s => s.SessionId == sessionId).OrderBy(s => s.MarkerId).ToListAsync(ct);

        var entries = players
            .Select(p => new MarkerMapping { MarkerId = p.MarkerId, Kind = MarkerKind.Player, PlayerId = p.Id, Nickname = p.Nickname, TeamId = p.TeamId })
            .Concat(spawns.Select(s => new MarkerMapping { MarkerId = s.MarkerId, Kind = MarkerKind.Spawn, SpawnPointId = s.Id, Name = s.Name, TeamId = s.TeamId }))
            .OrderBy(m => m.MarkerId)
            .ToList();
        var teams = session.Teams.OrderBy(t => t.TeamId).Select(t => new TeamInfo(t.TeamId, t.Name, t.Color)).ToList();
        return new MappingTable(sessionId, session.MappingVersion, entries, teams);
    }

    public async Task<PlayerRegistration> RegisterPlayerAsync(string sessionId, RegisterPlayerRequest request, CancellationToken ct = default)
    {
        var session = await LoadLobbyAsync(sessionId, ct);
        var nickname = ValidateNickname(request.Nickname);
        await EnsureTeamAsync(session.Id, request.TeamId, ct);
        await EnsureMarkerFreeAsync(session.Id, request.MarkerId, ct);
        await EnsureNicknameFreeAsync(session.Id, nickname, null, ct);

        var player = new Player
        {
            Id = Ids.New("player"),
            SessionId = session.Id,
            Nickname = nickname,
            TeamId = request.TeamId,
            MarkerId = request.MarkerId,
        };
        db.Players.Add(player);
        session.MappingVersion++;
        await db.SaveChangesAsync(ct);

        await BroadcastRosterAsync(session.Id, ct);
        return new PlayerRegistration(player.Id, player.Nickname, player.TeamId, player.MarkerId);
    }

    public async Task UpdatePlayerAsync(string sessionId, UpdatePlayerRequest request, CancellationToken ct = default)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);
        var player = await db.Players.SingleOrDefaultAsync(p => p.Id == request.PlayerId && p.SessionId == sessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.PlayerNotFound);

        var rosterChanged = request.Nickname is not null || request.TeamId is not null;
        if (rosterChanged && session.Status != SessionStatus.Lobby)
        {
            throw GameException.Conflict(ErrorCodes.WrongPhase, "Nickname and team can only change in the lobby.");
        }

        if (request.Nickname is not null)
        {
            var nickname = ValidateNickname(request.Nickname);
            await EnsureNicknameFreeAsync(sessionId, nickname, player.Id, ct);
            player.Nickname = nickname;
        }

        if (request.TeamId is not null)
        {
            await EnsureTeamAsync(sessionId, request.TeamId, ct);
            player.TeamId = request.TeamId;
        }

        var released = request.ReleaseDevice && player.DeviceLinkId is not null;
        if (released)
        {
            player.DeviceLinkId = null;
            if (session.AdminPlayerId == player.Id)
            {
                session.AdminPlayerId = null;
            }
        }

        if (rosterChanged)
        {
            session.MappingVersion++;
        }

        await db.SaveChangesAsync(ct);
        if (released)
        {
            await notifier.Kicked(player.Id, "DEVICE_RELEASED");
        }

        if (rosterChanged)
        {
            await BroadcastRosterAsync(sessionId, ct);
        }
    }

    public async Task RemovePlayerAsync(string sessionId, string playerId, CancellationToken ct = default)
    {
        var session = await LoadLobbyAsync(sessionId, ct);
        var player = await db.Players.SingleOrDefaultAsync(p => p.Id == playerId && p.SessionId == sessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.PlayerNotFound);
        db.Players.Remove(player);
        if (session.AdminPlayerId == player.Id)
        {
            session.AdminPlayerId = null;
        }

        session.MappingVersion++;
        await db.SaveChangesAsync(ct);

        await notifier.Kicked(playerId, "PLAYER_REMOVED");
        await BroadcastRosterAsync(sessionId, ct);
    }

    public async Task<SpawnPointInfo> RegisterSpawnPointAsync(string sessionId, RegisterSpawnPointRequest request, CancellationToken ct = default)
    {
        var session = await LoadLobbyAsync(sessionId, ct);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 64)
        {
            throw GameException.BadRequest(ErrorCodes.InvalidArgument, "name must be 1–64 characters.");
        }

        if (request.TeamId is not null)
        {
            await EnsureTeamAsync(sessionId, request.TeamId, ct);
        }

        await EnsureMarkerFreeAsync(sessionId, request.MarkerId, ct);
        var spawn = new SpawnPoint
        {
            Id = Ids.New("spawn"),
            SessionId = sessionId,
            MarkerId = request.MarkerId,
            Name = request.Name.Trim(),
            TeamId = request.TeamId,
        };
        db.SpawnPoints.Add(spawn);
        session.MappingVersion++;
        await db.SaveChangesAsync(ct);

        await BroadcastRosterAsync(sessionId, ct);
        return new SpawnPointInfo(spawn.Id, spawn.MarkerId, spawn.Name, spawn.TeamId);
    }

    public async Task RemoveSpawnPointAsync(string sessionId, string spawnPointId, CancellationToken ct = default)
    {
        var session = await LoadLobbyAsync(sessionId, ct);
        var spawn = await db.SpawnPoints.SingleOrDefaultAsync(s => s.Id == spawnPointId && s.SessionId == sessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.SpawnPointNotFound);
        db.SpawnPoints.Remove(spawn);
        session.MappingVersion++;
        await db.SaveChangesAsync(ct);
        await BroadcastRosterAsync(sessionId, ct);
    }

    public async Task UpdateGameConfigAsync(string sessionId, GameConfig? config, CancellationToken ct = default)
    {
        var session = await LoadLobbyAsync(sessionId, ct);
        ValidateConfig(config ?? throw GameException.BadRequest(ErrorCodes.InvalidArgument, "config is required."));
        session.Config = config.ToEntity();
        await db.SaveChangesAsync(ct);
        await notifier.GameConfigUpdated(sessionId, config);
    }

    private void ValidateConfig(GameConfig c)
    {
        var errors = new List<string>();
        if (c.CrosshairSize is <= 0 or > 1) errors.Add("crosshairSize must be in (0, 1].");
        if (c.FireWindowMs is < 10 or > 5000) errors.Add("fireWindowMs must be 10–5000.");
        if (c.RespawnSeconds is < 0 or > 3600) errors.Add("respawnSeconds must be 0–3600.");
        if (c.MatchDurationSeconds is < 10 or > 86_400) errors.Add("matchDurationSeconds must be 10–86400 or null.");
        if (c.MaxTargetsPerShot < 1 || c.MaxTargetsPerShot > fireRules.Value.MaxTargetsPerShotLimit)
            errors.Add($"maxTargetsPerShot must be 1–{fireRules.Value.MaxTargetsPerShotLimit}.");
        if (c.SpawnProtectionSeconds is < 0 or > 300) errors.Add("spawnProtectionSeconds must be 0–300.");
        if (!Enum.IsDefined(c.RespawnMode)) errors.Add("respawnMode is invalid.");
        if (!Enum.IsDefined(c.EliminationVisibility)) errors.Add("eliminationVisibility is invalid.");
        if (errors.Count > 0)
        {
            throw GameException.BadRequest(ErrorCodes.InvalidArgument, string.Join(" ", errors));
        }
    }

    private async Task<GameSession> LoadLobbyAsync(string sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);
        return session.Status == SessionStatus.Lobby ? session : throw GameException.Conflict(ErrorCodes.WrongPhase);
    }

    private static string ValidateNickname(string? nickname)
    {
        var trimmed = nickname?.Trim();
        return trimmed is { Length: >= 1 and <= 32 }
            ? trimmed
            : throw GameException.BadRequest(ErrorCodes.InvalidArgument, "nickname must be 1–32 characters.");
    }

    private async Task EnsureTeamAsync(string sessionId, string? teamId, CancellationToken ct)
    {
        if (teamId is null || !await db.Teams.AnyAsync(t => t.SessionId == sessionId && t.TeamId == teamId, ct))
        {
            throw GameException.BadRequest(ErrorCodes.TeamNotFound);
        }
    }

    private async Task EnsureMarkerFreeAsync(string sessionId, int markerId, CancellationToken ct)
    {
        if (markerId < 0 || markerId >= markers.Value.DictionarySize)
        {
            throw GameException.BadRequest(ErrorCodes.InvalidArgument, $"markerId must be 0–{markers.Value.DictionarySize - 1}.");
        }

        if (await db.Players.AnyAsync(p => p.SessionId == sessionId && p.MarkerId == markerId, ct)
            || await db.SpawnPoints.AnyAsync(s => s.SessionId == sessionId && s.MarkerId == markerId, ct))
        {
            throw GameException.Conflict(ErrorCodes.MarkerInUse);
        }
    }

    private async Task EnsureNicknameFreeAsync(string sessionId, string nickname, string? exceptPlayerId, CancellationToken ct)
    {
        if (await db.Players.AnyAsync(p => p.SessionId == sessionId && p.Nickname == nickname && p.Id != exceptPlayerId, ct))
        {
            throw GameException.Conflict(ErrorCodes.NicknameTaken);
        }
    }

    private async Task BroadcastRosterAsync(string sessionId, CancellationToken ct) =>
        await notifier.RosterUpdated(sessionId, await GetMappingTableAsync(sessionId, ct));
}
