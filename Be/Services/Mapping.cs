using Be.Contracts;
using Be.Data;

namespace Be.Services;

public static class Mapping
{
    public static GameConfig ToContract(this SessionConfig c) => new()
    {
        CrosshairSize = c.CrosshairSize,
        FireWindowMs = c.FireWindowMs,
        RespawnSeconds = c.RespawnSeconds,
        MatchDurationSeconds = c.MatchDurationSeconds,
        MaxTargetsPerShot = c.MaxTargetsPerShot,
        RespawnMode = c.RespawnMode,
        SpawnProtectionSeconds = c.SpawnProtectionSeconds,
        EliminationVisibility = c.EliminationVisibility,
        FriendlyFire = c.FriendlyFire,
    };

    public static SessionConfig ToEntity(this GameConfig c) => new()
    {
        CrosshairSize = c.CrosshairSize,
        FireWindowMs = c.FireWindowMs,
        RespawnSeconds = c.RespawnSeconds,
        MatchDurationSeconds = c.MatchDurationSeconds,
        MaxTargetsPerShot = c.MaxTargetsPerShot,
        RespawnMode = c.RespawnMode,
        SpawnProtectionSeconds = c.SpawnProtectionSeconds,
        EliminationVisibility = c.EliminationVisibility,
        FriendlyFire = c.FriendlyFire,
    };

    public static PlayerState ToState(this Player p) => new()
    {
        PlayerId = p.Id,
        TeamId = p.TeamId,
        Status = p.Status,
        HitAt = p.HitAt,
        RespawnAt = p.RespawnAt,
        ProtectedUntil = p.ProtectedUntil,
    };
}
