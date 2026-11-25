using System.ComponentModel.DataAnnotations;

namespace Be.Contracts;

/// <summary>Team definition used when creating a session.</summary>
public sealed record TeamDefinition(
    [Required, RegularExpression("^[a-z0-9_-]{1,32}$")] string TeamId,
    [Required, StringLength(64, MinimumLength = 1)] string Name,
    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")] string Color);

/// <summary>Creates a new session. Without <see cref="Teams"/> the session gets a blue and a red team.</summary>
public sealed record CreateSessionRequest(IReadOnlyList<TeamDefinition>? Teams);

public sealed record CreateSessionResponse(string SessionId, string SessionCode, string JoinQrPayload, string AdminToken);

public sealed record JoinSessionRequest([Required, StringLength(16, MinimumLength = 4)] string SessionCode);

public sealed record JoinSessionResponse(string SessionId, string AccessToken, string Role);

public sealed record DeviceLinkRequest([Required, Range(0, int.MaxValue)] int? MarkerId);

public sealed record DeviceLinkResponse(
    bool Success,
    string PlayerId,
    string Nickname,
    string TeamId,
    string Role,
    string UpgradedToken);
