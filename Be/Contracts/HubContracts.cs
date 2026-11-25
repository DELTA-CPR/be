using Be.Domain;

namespace Be.Contracts;

// ---------- Configuration and roster ----------

public sealed record GameConfig
{
    public double CrosshairSize { get; init; }
    public int FireWindowMs { get; init; }
    public int RespawnSeconds { get; init; }
    public int? MatchDurationSeconds { get; init; }
    public int MaxTargetsPerShot { get; init; }
    public RespawnMode RespawnMode { get; init; }
    public int SpawnProtectionSeconds { get; init; }
    public EliminationVisibility EliminationVisibility { get; init; }
    public bool FriendlyFire { get; init; }
}

public sealed record MarkerMapping
{
    public int MarkerId { get; init; }
    public MarkerKind Kind { get; init; }
    public string? PlayerId { get; init; }
    public string? Nickname { get; init; }
    public string? SpawnPointId { get; init; }
    public string? Name { get; init; }
    public string? TeamId { get; init; }
}

public sealed record TeamInfo(string TeamId, string Name, string Color);

public sealed record MappingTable(string SessionId, int Version, IReadOnlyList<MarkerMapping> Markers, IReadOnlyList<TeamInfo> Teams);

public sealed record RegisterPlayerRequest(string Nickname, string TeamId, int MarkerId);

public sealed record PlayerRegistration(string PlayerId, string Nickname, string TeamId, int MarkerId);

public sealed record UpdatePlayerRequest(string PlayerId, string? Nickname, string? TeamId, bool ReleaseDevice = false);

public sealed record RegisterSpawnPointRequest(int MarkerId, string Name, string? TeamId);

public sealed record SpawnPointInfo(string SpawnPointId, int MarkerId, string Name, string? TeamId);

// ---------- State snapshot ----------

public sealed record PlayerState
{
    public required string PlayerId { get; init; }
    public string? TeamId { get; init; }
    public PlayerStatus Status { get; init; }
    public DateTime? HitAt { get; init; }
    public DateTime? RespawnAt { get; init; }
    public DateTime? ProtectedUntil { get; init; }
}

public sealed record TeamScore(string TeamId, int Eliminations);

/// <summary>Live team totals. <see cref="Version"/> grows with every scored elimination; keep the highest seen.</summary>
public sealed record Scoreboard(string SessionId, int Version, IReadOnlyList<TeamScore> Teams);

public sealed record GameStateSnapshot
{
    public required string SessionId { get; init; }
    public SessionStatus Status { get; init; }
    public required GameConfig Config { get; init; }
    public PlayerState? Self { get; init; }
    public required IReadOnlyList<PlayerState> Players { get; init; }
    public required Scoreboard Score { get; init; }
    public int MappingVersion { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EndsAt { get; init; }
    public DateTime ServerTime { get; init; }
}

public sealed record ServerTimeResult(DateTime ServerTime);

// ---------- Fire ----------

public sealed record ShotTarget(string PlayerId, IReadOnlyList<int>? MarkerIds);

public sealed record ShotRequest
{
    public string? SessionId { get; init; }
    public string? ShotId { get; init; }

    /// <summary>Client estimate of the fire press, corrected by the clock offset from <c>GetServerTime</c>.</summary>
    public DateTime? Timestamp { get; init; }

    public IReadOnlyList<ShotTarget>? Targets { get; init; }
}

public sealed record TargetResult
{
    public required string PlayerId { get; init; }
    public TargetOutcome Outcome { get; init; }
    public string? Reason { get; init; }
    public DateTime? RespawnAt { get; init; }
}

public sealed record ShotResult
{
    public required string ShotId { get; init; }

    /// <summary>True when at least one target was eliminated.</summary>
    public bool Accepted { get; init; }

    /// <summary>True when this is the stored result of an earlier request with the same shotId.</summary>
    public bool Duplicate { get; init; }

    public DateTime ServerTime { get; init; }

    /// <summary>Set when the whole request was rejected; <see cref="Targets"/> is then empty.</summary>
    public string? ShotRejectedReason { get; init; }

    public IReadOnlyList<TargetResult> Targets { get; init; } = [];
}

public sealed record ConfirmSpawnResult(bool Confirmed, PlayerStatus? State, string? Reason, DateTime? ProtectedUntil);

// ---------- Server → client events ----------

public sealed record GameStartedEvent(DateTime StartedAt, DateTime? EndsAt, GameConfig Config);

public sealed record GameEndedEvent(DateTime EndedAt, GameEndReason Reason, Scoreboard Score);

public sealed record PlayerHitEvent
{
    public string Event => "PlayerHit";
    public required string PlayerId { get; init; }
    public PlayerStatus Status { get; init; }
    public DateTime HitAt { get; init; }

    /// <summary>Timer mode: when the server respawns the player. QR mode: earliest time <c>ConfirmSpawn</c> is accepted.</summary>
    public DateTime? RespawnAt { get; init; }

    public RespawnMode RespawnMode { get; init; }
    public required string ShotId { get; init; }
    public required string ByPlayerId { get; init; }
}

public sealed record EliminationEvent
{
    public required string PlayerId { get; init; }
    public required string TeamId { get; init; }
    public required string ByPlayerId { get; init; }
    public required string ByTeamId { get; init; }
    public DateTime At { get; init; }
    public DateTime? RespawnAt { get; init; }
    public bool FriendlyFire { get; init; }
    public required string ShotId { get; init; }
}

public sealed record PlayerRespawnedEvent(string PlayerId, PlayerStatus Status, DateTime At, DateTime? ProtectedUntil);

public sealed record PlayerStateEvent(string PlayerId, PlayerStatus Status, DateTime? Until);
