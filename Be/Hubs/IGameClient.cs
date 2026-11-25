using Be.Contracts;

namespace Be.Hubs;

/// <summary>Server → client callbacks.</summary>
public interface IGameClient
{
    Task GameConfigUpdated(GameConfig config);
    Task RosterUpdated(MappingTable table);
    Task GameStarted(GameStartedEvent e);
    Task GameEnded(GameEndedEvent e);

    /// <summary>Private to the eliminated player.</summary>
    Task PlayerHit(PlayerHitEvent e);

    /// <summary>Recipients follow <c>eliminationVisibility</c>.</summary>
    Task PlayerEliminated(EliminationEvent e);

    Task PlayerRespawned(PlayerRespawnedEvent e);
    Task PlayerStateChanged(PlayerStateEvent e);

    /// <summary>Team totals after a shot that changed the score. Sent to the whole session.</summary>
    Task ScoreUpdated(Scoreboard score);

    Task Kicked(string reason);
}

public static class HubGroups
{
    public static string Session(string sessionId) => $"session:{sessionId}";
    public static string Admins(string sessionId) => $"admins:{sessionId}";
}
