using System.Security.Claims;
using Be.Domain;
using Microsoft.AspNetCore.SignalR;

namespace Be.Infrastructure;

public static class GameClaims
{
    public const string Subject = "sub";
    public const string SessionId = "sessionId";
    public const string Role = "role";
    public const string DeviceLink = "lnk";
}

/// <summary>The authenticated caller, derived only from token claims.</summary>
public sealed record CallerIdentity(string Subject, string SessionId, string Role, string? PlayerId, Guid? DeviceLinkId)
{
    public bool IsAdmin => Role == Roles.Admin;

    public static CallerIdentity From(ClaimsPrincipal? user)
    {
        var sub = user?.FindFirst(GameClaims.Subject)?.Value;
        var sessionId = user?.FindFirst(GameClaims.SessionId)?.Value;
        var role = user?.FindFirst(GameClaims.Role)?.Value;
        if (sub is null || sessionId is null || role is null)
        {
            throw GameException.Unauthorized();
        }

        Guid? link = Guid.TryParse(user!.FindFirst(GameClaims.DeviceLink)?.Value, out var l) ? l : null;
        // A player token always names a player; an admin token names one only after the admin linked a marker.
        var playerId = role == Roles.Player || (role == Roles.Admin && link is not null) ? sub : null;
        return new CallerIdentity(sub, sessionId, role, playerId, link);
    }

    public string RequireLinkedPlayer() =>
        PlayerId ?? throw GameException.Forbidden(detail: "Only a device linked to a registered player can do this.");

    public void RequireAdmin()
    {
        if (!IsAdmin)
        {
            throw GameException.Forbidden();
        }
    }
}

/// <summary>Uses the token's <c>sub</c> claim as the SignalR user ID so <c>Clients.User(playerId)</c> reaches a player.</summary>
public sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirst(GameClaims.Subject)?.Value;
}
