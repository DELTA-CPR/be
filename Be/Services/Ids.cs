using System.Security.Cryptography;

namespace Be.Services;

public static class Ids
{
    // No 0/O/1/I so that typed codes are unambiguous.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New(string prefix) => $"{prefix}_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8))}";

    public static string NewSessionCode(int length = 6) => RandomNumberGenerator.GetString(CodeAlphabet, length);
}

public static class ServerClock
{
    /// <summary>
    /// Server time truncated to whole milliseconds, so a timestamp returned in a <c>ShotResult</c> or event is
    /// exactly what PostgreSQL stores (microsecond precision) and what a later <c>GetGameState</c> returns.
    /// </summary>
    public static DateTime UtcNow(this TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
