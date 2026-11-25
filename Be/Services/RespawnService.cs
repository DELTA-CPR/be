using Be.Contracts;
using Be.Data;
using Be.Domain;
using Be.Infrastructure;
using Be.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Be.Services;

/// <summary>
/// Returns eliminated players to the game: automatically when <c>respawnAt</c> passes (timer mode) or when the
/// player scans a spawn point after <c>respawnAt</c> (QR mode). Every transition is a conditional UPDATE, so with
/// several server instances each respawn is handled exactly once.
/// </summary>
public sealed class RespawnService(GameDbContext db, GameNotifier notifier, MatchService match, TimeProvider time)
{
    /// <summary>One sweep: timer respawns, expired spawn protection and match timeouts.</summary>
    public async Task SweepAsync(CancellationToken ct = default)
    {
        var now = time.UtcNow();

        var due = await (
            from p in db.Players.AsNoTracking()
            join s in db.Sessions.AsNoTracking() on p.SessionId equals s.Id
            where s.Status == SessionStatus.Running
                && s.Config.RespawnMode == RespawnMode.Timer
                && p.Status == PlayerStatus.Respawning
                && p.RespawnAt <= now
            select p.Id).ToListAsync(ct);
        foreach (var playerId in due)
        {
            await TryRespawnAsync(playerId, now, ct);
        }

        var protectionOver = await (
            from p in db.Players.AsNoTracking()
            join s in db.Sessions.AsNoTracking() on p.SessionId equals s.Id
            where s.Status == SessionStatus.Running && p.Status == PlayerStatus.Protected && p.ProtectedUntil <= now
            select p.Id).ToListAsync(ct);
        foreach (var playerId in protectionOver)
        {
            await TryEndProtectionAsync(playerId, now, ct);
        }

        var timedOut = await db.Sessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Running && s.EndsAt <= now)
            .Select(s => s.Id)
            .ToListAsync(ct);
        foreach (var sessionId in timedOut)
        {
            await match.TryEndAsync(sessionId, GameEndReason.Timeout, ct);
        }
    }

    public async Task<ConfirmSpawnResult> ConfirmSpawnAsync(CallerIdentity caller, int spawnMarkerId, CancellationToken ct = default)
    {
        var playerId = caller.RequireLinkedPlayer();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == caller.SessionId, ct)
            ?? throw GameException.NotFound(ErrorCodes.SessionNotFound);
        if (session.Status != SessionStatus.Running)
        {
            return new ConfirmSpawnResult(false, null, ErrorCodes.SessionNotActive, null);
        }

        var player = await db.Players.AsNoTracking().SingleAsync(p => p.Id == playerId, ct);
        if (session.Config.RespawnMode != RespawnMode.Qr)
        {
            return new ConfirmSpawnResult(false, player.Status, ErrorCodes.SpawnConfirmationDisabled, null);
        }

        var spawn = await db.SpawnPoints.AsNoTracking()
            .SingleOrDefaultAsync(s => s.SessionId == session.Id && s.MarkerId == spawnMarkerId, ct);
        if (spawn is null)
        {
            return new ConfirmSpawnResult(false, player.Status, ErrorCodes.NotASpawnMarker, null);
        }

        if (spawn.TeamId is not null && spawn.TeamId != player.TeamId)
        {
            return new ConfirmSpawnResult(false, player.Status, ErrorCodes.WrongTeamSpawn, null);
        }

        var now = time.UtcNow();
        if (player.Status != PlayerStatus.Respawning || player.RespawnAt > now)
        {
            return new ConfirmSpawnResult(false, player.Status, ErrorCodes.RespawnNotReady, null);
        }

        var respawned = await TryRespawnAsync(playerId, now, ct);
        return respawned is null
            ? new ConfirmSpawnResult(false, null, ErrorCodes.RespawnNotReady, null)
            : new ConfirmSpawnResult(true, respawned.Status, null, respawned.ProtectedUntil);
    }

    private async Task<Player?> TryRespawnAsync(string playerId, DateTime now, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(p => p.Id == playerId, ct);
        if (player is null)
        {
            return null;
        }

        var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == player.SessionId, ct);
        var protection = session.Config.SpawnProtectionSeconds;
        var status = protection > 0 ? PlayerStatus.Protected : PlayerStatus.Alive;
        DateTime? protectedUntil = protection > 0 ? now.AddSeconds(protection) : null;

        var changed = await db.Players
            .Where(p => p.Id == playerId && p.Status == PlayerStatus.Respawning && p.RespawnAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, status)
                .SetProperty(p => p.RespawnAt, (DateTime?)null)
                .SetProperty(p => p.ProtectedUntil, protectedUntil), ct);
        if (changed == 0)
        {
            return null;
        }

        await notifier.PlayerRespawned(session, player.Id, player.TeamId, new PlayerRespawnedEvent(player.Id, status, now, protectedUntil), ct);
        if (protectedUntil is not null)
        {
            await notifier.PlayerStateChanged(session, player.Id, player.TeamId, new PlayerStateEvent(player.Id, status, protectedUntil), ct);
        }

        player.Status = status;
        player.RespawnAt = null;
        player.ProtectedUntil = protectedUntil;
        return player;
    }

    private async Task TryEndProtectionAsync(string playerId, DateTime now, CancellationToken ct)
    {
        var changed = await db.Players
            .Where(p => p.Id == playerId && p.Status == PlayerStatus.Protected && p.ProtectedUntil <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PlayerStatus.Alive)
                .SetProperty(p => p.ProtectedUntil, (DateTime?)null), ct);
        if (changed == 0)
        {
            return;
        }

        var player = await db.Players.AsNoTracking().SingleAsync(p => p.Id == playerId, ct);
        var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == player.SessionId, ct);
        await notifier.PlayerStateChanged(session, player.Id, player.TeamId, new PlayerStateEvent(player.Id, PlayerStatus.Alive, null), ct);
    }
}

/// <summary>Runs <see cref="RespawnService.SweepAsync"/> on a fixed interval. Respawn times live in PostgreSQL, so timers survive restarts.</summary>
public sealed class RespawnSweeper(IServiceScopeFactory scopes, IOptions<RespawnSweeperOptions> options, TimeProvider time, ILogger<RespawnSweeper> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.IntervalMs), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RespawnService>().SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep sweeping: one failed tick (e.g. a DB blip) must not stop all future respawns.
                logger.LogError(ex, "Respawn sweep failed");
            }
        }
    }
}
