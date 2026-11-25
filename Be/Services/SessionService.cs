using Be.Contracts;
using Be.Data;
using Be.Domain;
using Be.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Be.Services;

/// <summary>REST bootstrap: create a session, join by code, link a device to a registered player.</summary>
public sealed class SessionService(GameDbContext db, TokenService tokens, TimeProvider time)
{
    private static readonly TeamDefinition[] DefaultTeams =
    [
        new("blue", "Blue Team", "#1E66F5"),
        new("red", "Red Team", "#D20F39"),
    ];

    public async Task<CreateSessionResponse> CreateAsync(CreateSessionRequest? request, CancellationToken ct = default)
    {
        var teams = request?.Teams is { Count: > 0 } t ? t : DefaultTeams;
        if (teams.Count < 2 || teams.Select(x => x.TeamId).Distinct().Count() != teams.Count)
        {
            throw GameException.BadRequest(ErrorCodes.InvalidArgument, "At least two teams with distinct teamIds are required.");
        }

        var sessionId = Ids.New("game");
        var code = await NewUniqueCodeAsync(ct);
        var adminSubject = Ids.New("admin");
        db.Sessions.Add(new GameSession
        {
            Id = sessionId,
            Code = code,
            AdminSubject = adminSubject,
            CreatedAt = time.UtcNow(),
            Teams = teams.Select(x => new Team { SessionId = sessionId, TeamId = x.TeamId, Name = x.Name, Color = x.Color }).ToList(),
        });
        await db.SaveChangesAsync(ct);

        return new CreateSessionResponse(
            sessionId,
            code,
            $"gameoftag://join?code={code}",
            tokens.Issue(adminSubject, sessionId, Roles.Admin));
    }

    public async Task<JoinSessionResponse> JoinAsync(JoinSessionRequest request, CancellationToken ct = default)
    {
        var code = request.SessionCode.Trim().ToUpperInvariant();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Code == code, ct)
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);
        if (session.Status == SessionStatus.Ended)
        {
            throw GameException.Conflict(ErrorCodes.SessionEnded);
        }

        return new JoinSessionResponse(session.Id, tokens.Issue(Ids.New("pending"), session.Id, Roles.Pending), Roles.Pending);
    }

    public async Task<DeviceLinkResponse> LinkDeviceAsync(CallerIdentity caller, string sessionId, int markerId, CancellationToken ct = default)
    {
        if (caller.SessionId != sessionId)
        {
            throw GameException.Forbidden(ErrorCodes.SessionMismatch);
        }

        if (caller.Role == Roles.Player)
        {
            throw GameException.Conflict(ErrorCodes.PlayerAlreadyLinked, "This token is already linked to a player.");
        }

        var session = await db.Sessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);
        var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(p => p.SessionId == sessionId && p.MarkerId == markerId, ct);
        if (player is null)
        {
            var isSpawn = await db.SpawnPoints.AnyAsync(s => s.SessionId == sessionId && s.MarkerId == markerId, ct);
            throw isSpawn
                ? GameException.BadRequest(ErrorCodes.MarkerIsSpawnPoint)
                : GameException.NotFound(ErrorCodes.MarkerNotRegistered);
        }

        // Conditional update: two devices scanning the same marker at once cannot both win.
        var linkId = Guid.NewGuid();
        var linked = await db.Players
            .Where(p => p.Id == player.Id && p.DeviceLinkId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeviceLinkId, linkId), ct);
        if (linked == 0)
        {
            throw GameException.Conflict(ErrorCodes.PlayerAlreadyLinked);
        }

        if (caller.IsAdmin)
        {
            session.AdminPlayerId = player.Id;
            await db.SaveChangesAsync(ct);
        }

        var role = caller.IsAdmin ? Roles.Admin : Roles.Player;
        return new DeviceLinkResponse(true, player.Id, player.Nickname, player.TeamId, role, tokens.Issue(player.Id, sessionId, role, linkId));
    }

    private async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = Ids.NewSessionCode();
            if (!await db.Sessions.AnyAsync(s => s.Code == code, ct))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique session code.");
    }
}
