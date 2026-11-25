namespace Be.Domain;

public enum PlayerStatus
{
    Alive,
    Respawning,
    Protected,
}

public enum SessionStatus
{
    Lobby,
    Running,
    Ended,
}

public enum MarkerKind
{
    Player,
    Spawn,
}

public enum TargetOutcome
{
    Eliminated,
    Rejected,
}

/// <summary>Who receives <c>PlayerEliminated</c>, <c>PlayerRespawned</c> and <c>PlayerStateChanged</c>.</summary>
public enum EliminationVisibility
{
    /// <summary>Everyone in the session.</summary>
    All,

    /// <summary>The victim's team (and the session admin).</summary>
    Team,

    /// <summary>Only the eliminated player (and the session admin).</summary>
    Victim,
}

/// <summary>How an eliminated player returns to the game.</summary>
public enum RespawnMode
{
    /// <summary>The server respawns the player automatically at <c>respawnAt</c>.</summary>
    Timer,

    /// <summary>The player must scan a spawn point marker (<c>ConfirmSpawn</c>) once <c>respawnAt</c> has passed.</summary>
    Qr,
}

public enum GameEndReason
{
    Admin,
    Timeout,
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Player = "player";
    public const string Pending = "pending";
}
