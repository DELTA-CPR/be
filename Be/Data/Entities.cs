using Be.Domain;

namespace Be.Data;

public class GameSession
{
    public required string Id { get; set; }
    public required string Code { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Lobby;
    public SessionConfig Config { get; set; } = new();

    /// <summary>Token subject of the admin who created the session (before the admin links a player marker).</summary>
    public required string AdminSubject { get; set; }

    /// <summary>Player linked by the admin's own device, if the admin also plays.</summary>
    public string? AdminPlayerId { get; set; }

    /// <summary>Incremented on every roster, team or spawn point change.</summary>
    public int MappingVersion { get; set; } = 1;

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public GameEndReason? EndReason { get; set; }

    public List<Team> Teams { get; set; } = [];
}

/// <summary>Admin-controlled match configuration, frozen when the match starts.</summary>
public class SessionConfig
{
    public double CrosshairSize { get; set; } = 0.12;
    public int FireWindowMs { get; set; } = 150;
    public int RespawnSeconds { get; set; } = 300;
    public int? MatchDurationSeconds { get; set; } = 1800;
    public int MaxTargetsPerShot { get; set; } = 8;
    public RespawnMode RespawnMode { get; set; } = RespawnMode.Timer;
    public int SpawnProtectionSeconds { get; set; }
    public EliminationVisibility EliminationVisibility { get; set; } = EliminationVisibility.All;
    public bool FriendlyFire { get; set; }
}

public class Team
{
    public required string SessionId { get; set; }
    public required string TeamId { get; set; }
    public required string Name { get; set; }
    public required string Color { get; set; }
}

public class Player
{
    public required string Id { get; set; }
    public required string SessionId { get; set; }
    public required string Nickname { get; set; }
    public required string TeamId { get; set; }

    /// <summary>The player's single ArUco marker.</summary>
    public int MarkerId { get; set; }

    public PlayerStatus Status { get; set; } = PlayerStatus.Alive;
    public DateTime? HitAt { get; set; }
    public DateTime? RespawnAt { get; set; }
    public DateTime? ProtectedUntil { get; set; }

    /// <summary>Identifies the device currently linked to this player; carried in the player's token as <c>lnk</c>.</summary>
    public Guid? DeviceLinkId { get; set; }
}

public class SpawnPoint
{
    public required string Id { get; set; }
    public required string SessionId { get; set; }
    public int MarkerId { get; set; }
    public required string Name { get; set; }
    public string? TeamId { get; set; }
}

/// <summary>
/// One confirmed elimination. The audit trail behind every score point: match, shot, shooter, target,
/// both teams at the time of the hit and the server time.
/// </summary>
public class Elimination
{
    public long Id { get; set; }
    public required string SessionId { get; set; }
    public required string ShotId { get; set; }
    public required string ShooterId { get; set; }
    public required string ShooterTeamId { get; set; }
    public required string TargetId { get; set; }
    public required string TargetTeamId { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? RespawnAt { get; set; }
    public bool FriendlyFire { get; set; }

    /// <summary>Whether this elimination adds a point to <see cref="ShooterTeamId"/>. Friendly-fire eliminations do not.</summary>
    public bool CountsForScore { get; set; }
}

/// <summary>Idempotency store: the stored <c>ShotResult</c> for each (session, shooter, shotId).</summary>
public class ShotRecord
{
    public required string SessionId { get; set; }
    public required string ShooterId { get; set; }
    public required string ShotId { get; set; }
    public DateTime ReceivedAt { get; set; }

    /// <summary>True when the shot passed request-level validation; used for the per-shooter rate limit.</summary>
    public bool CountsForRateLimit { get; set; }

    public required string ResultJson { get; set; }
}
