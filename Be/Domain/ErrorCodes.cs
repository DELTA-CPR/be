namespace Be.Domain;

/// <summary>Stable error codes shared by REST ProblemDetails, hub exceptions and per-target shot outcomes.</summary>
public static class ErrorCodes
{
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string SessionNotFound = "SESSION_NOT_FOUND";
    public const string SessionMismatch = "SESSION_MISMATCH";
    public const string SessionNotActive = "SESSION_NOT_ACTIVE";
    public const string SessionEnded = "SESSION_ENDED";
    public const string WrongPhase = "WRONG_PHASE";
    public const string ShooterNotAlive = "SHOOTER_NOT_ALIVE";
    public const string RateLimited = "RATE_LIMITED";
    public const string TimestampOutOfRange = "TIMESTAMP_OUT_OF_RANGE";
    public const string TargetNotFound = "TARGET_NOT_FOUND";
    public const string TargetIsSelf = "TARGET_IS_SELF";
    public const string SameTeam = "SAME_TEAM";
    public const string MarkerMismatch = "MARKER_MISMATCH";
    public const string MarkerIsSpawnPoint = "MARKER_IS_SPAWN_POINT";
    public const string TargetNotAlive = "TARGET_NOT_ALIVE";
    public const string MarkerInUse = "MARKER_IN_USE";
    public const string MarkerNotRegistered = "MARKER_NOT_REGISTERED";
    public const string NicknameTaken = "NICKNAME_TAKEN";
    public const string TeamNotFound = "TEAM_NOT_FOUND";
    public const string PlayerNotFound = "PLAYER_NOT_FOUND";
    public const string PlayerAlreadyLinked = "PLAYER_ALREADY_LINKED";
    public const string SpawnPointNotFound = "SPAWN_POINT_NOT_FOUND";
    public const string NotEnoughTeams = "NOT_ENOUGH_TEAMS";
    public const string NotASpawnMarker = "NOT_A_SPAWN_MARKER";
    public const string WrongTeamSpawn = "WRONG_TEAM_SPAWN";
    public const string RespawnNotReady = "RESPAWN_NOT_READY";
    public const string SpawnConfirmationDisabled = "SPAWN_CONFIRMATION_DISABLED";
}

/// <summary>
/// A protocol, authorization or state error with a stable <see cref="Code"/>.
/// Translated to ProblemDetails for REST and to <c>HubException</c> for SignalR.
/// Business outcomes (for example a rejected shot target) are returned in result objects instead.
/// </summary>
public sealed class GameException(string code, int httpStatus, string? detail = null)
    : Exception(detail ?? code)
{
    public string Code { get; } = code;
    public int HttpStatus { get; } = httpStatus;

    public static GameException BadRequest(string code, string? detail = null) => new(code, StatusCodes.Status400BadRequest, detail);
    public static GameException Unauthorized(string? detail = null) => new(ErrorCodes.Unauthenticated, StatusCodes.Status401Unauthorized, detail);
    public static GameException Forbidden(string code = ErrorCodes.Forbidden, string? detail = null) => new(code, StatusCodes.Status403Forbidden, detail);
    public static GameException NotFound(string code, string? detail = null) => new(code, StatusCodes.Status404NotFound, detail);
    public static GameException Conflict(string code, string? detail = null) => new(code, StatusCodes.Status409Conflict, detail);
}
