using System.ComponentModel.DataAnnotations;

namespace Be.Options;

/// <summary>
/// Server-side anti-abuse limits for <c>Fire</c>. This is the single place where the values the server
/// actually decides by are defined (bound from the <c>FireRules</c> configuration section).
/// </summary>
public sealed class FireRulesOptions
{
    public const string Section = "FireRules";

    /// <summary>Minimum time between two processed shots of the same shooter.</summary>
    [Range(0, 10_000)]
    public int MinShotIntervalMs { get; set; } = 250;

    /// <summary>Maximum allowed difference between the client's (offset-corrected) timestamp and server receive time.</summary>
    [Range(0, 60_000)]
    public int MaxClockSkewMs { get; set; } = 2000;

    /// <summary>Upper bound an admin may set for <c>maxTargetsPerShot</c>.</summary>
    [Range(1, 64)]
    public int MaxTargetsPerShotLimit { get; set; } = 16;

    [Range(1, 256)]
    public int MaxShotIdLength { get; set; } = 128;
}

public sealed class MarkerOptions
{
    public const string Section = "Markers";

    /// <summary>Number of IDs in the ArUco dictionary (DICT_4X4_50 → 50, IDs 0–49).</summary>
    [Range(1, 1024)]
    public int DictionarySize { get; set; } = 50;
}

public sealed class RespawnSweeperOptions
{
    public const string Section = "RespawnSweeper";

    public bool Enabled { get; set; } = true;

    [Range(50, 10_000)]
    public int IntervalMs { get; set; } = 250;
}

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "game-of-tag";
    public string Audience { get; set; } = "game-of-tag-clients";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Supply via user-secrets or the <c>Jwt__SigningKey</c> environment variable.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = "";

    [Range(1, 168)]
    public int TokenLifetimeHours { get; set; } = 24;
}
