using System.Text.Json;
using Be.Contracts;
using Be.Data;
using Be.Domain;
using Be.Infrastructure;
using Be.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Be.Tests;

public sealed class FireTests(GameFactory factory)
{
    [Fact]
    public async Task Valid_hit_eliminates_target_scores_one_point_and_sends_exactly_one_PlayerHit()
    {
        await using var m = await TestMatch.StartAsync(factory);

        var result = await m.Fire("Anna", m.ShotAt("Cyril"));

        Assert.True(result.Accepted);
        Assert.False(result.Duplicate);
        var target = Assert.Single(result.Targets);
        Assert.Equal(TargetOutcome.Eliminated, target.Outcome);
        Assert.NotNull(target.RespawnAt);

        var state = await m.PlayerState("Cyril");
        Assert.Equal(PlayerStatus.Respawning, state.Status);
        Assert.Equal(target.RespawnAt, state.RespawnAt);

        var score = await m.Score();
        Assert.Equal(1, score.Of("blue"));
        Assert.Equal(0, score.Of("red"));
        Assert.Equal(1, score.Version);

        var hit = await m["Cyril"].WaitFor<PlayerHitEvent>("PlayerHit");
        Assert.Equal(m.Id("Cyril"), hit.PlayerId);
        Assert.Equal(PlayerStatus.Respawning, hit.Status);
        Assert.Equal(result.ShotId, hit.ShotId);
        Assert.Equal(target.RespawnAt, hit.RespawnAt);
        Assert.Equal(RespawnMode.Timer, hit.RespawnMode);

        // Everybody (visibility "all") hears about the elimination and gets the new scoreboard.
        await m["Dana"].WaitFor<EliminationEvent>("PlayerEliminated", e => e.PlayerId == m.Id("Cyril"));
        var update = await m["Bob"].WaitFor<Scoreboard>("ScoreUpdated");
        Assert.Equal(1, update.Of("blue"));

        await Task.Delay(200);
        Assert.Equal(1, m["Cyril"].Count("PlayerHit"));
        Assert.Equal(0, m["Dana"].Count("PlayerHit"));
        Assert.Equal(0, m["Anna"].Count("PlayerHit"));
        Assert.Equal(1, await factory.CountEliminationsAsync(m.SessionId));
    }

    [Fact]
    public async Task Scenario_from_the_brief_mixed_result_and_retry_keep_score_at_4_to_4()
    {
        await using var m = await TestMatch.StartAsync(factory);

        // Dana is already waiting for respawn (blue's 3rd point); red leads 4 : 3.
        Assert.True((await m.Fire("Bob", m.ShotAt("Dana"))).Accepted);
        await SeedScoredEliminationsAsync(m, shooter: "Anna", target: "Cyril", count: 2);
        await SeedScoredEliminationsAsync(m, shooter: "Cyril", target: "Anna", count: 4);
        var before = await m.Score();
        Assert.Equal((3, 4), (before.Of("blue"), before.Of("red")));

        // One Fire at two red players: Cyril is alive, Dana is respawning.
        var request = m.ShotAt("Cyril", "Dana");
        var result = await m.Fire("Anna", request);

        Assert.True(result.Accepted);
        Assert.Null(result.ShotRejectedReason);
        var cyril = result.Targets.Single(t => t.PlayerId == m.Id("Cyril"));
        var dana = result.Targets.Single(t => t.PlayerId == m.Id("Dana"));
        Assert.Equal(TargetOutcome.Eliminated, cyril.Outcome);
        Assert.Equal(TargetOutcome.Rejected, dana.Outcome);
        Assert.Equal(ErrorCodes.TargetNotAlive, dana.Reason);

        var after = await m.Score();
        Assert.Equal((4, 4), (after.Of("blue"), after.Of("red")));

        // The phone lost the answer and sends the same request again.
        var retry = await m.Fire("Anna", request);
        Assert.True(retry.Duplicate);
        Assert.Equivalent(result with { Duplicate = true }, retry, strict: true);
        var final = await m.Score();
        Assert.Equal((4, 4), (final.Of("blue"), final.Of("red")));
        Assert.Equal(after.Version, final.Version);

        // The JSON the FE receives for this mixed result.
        var json = JsonSerializer.Serialize(result, GameJson.Options);
        Assert.Contains("\"outcome\":\"eliminated\"", json);
        Assert.Contains("\"outcome\":\"rejected\"", json);
        Assert.Contains("\"reason\":\"TARGET_NOT_ALIVE\"", json);
    }

    [Fact]
    public async Task Same_shotId_delivered_twice_concurrently_eliminates_once()
    {
        await using var m = await TestMatch.StartAsync(factory);
        var second = await m.ConnectAgainAsync("Anna");
        var request = m.ShotAt("Cyril");

        var results = await Task.WhenAll(
            m["Anna"].Invoke<ShotResult>("Fire", request),
            second.Invoke<ShotResult>("Fire", request));

        Assert.All(results, r =>
        {
            Assert.True(r.Accepted);
            Assert.Equal(TargetOutcome.Eliminated, Assert.Single(r.Targets).Outcome);
        });
        Assert.Single(results, r => r.Duplicate);
        Assert.Equal(results[0].Targets[0].RespawnAt, results[1].Targets[0].RespawnAt);
        Assert.Equal(results[0].ServerTime, results[1].ServerTime);

        Assert.Equal(1, await factory.CountEliminationsAsync(m.SessionId, m.Id("Cyril")));
        Assert.Equal(1, (await m.Score()).Of("blue"));
        await Task.Delay(200);
        Assert.Equal(1, m["Cyril"].Count("PlayerHit"));
    }

    [Fact]
    public async Task Same_shotId_hammered_in_parallel_at_service_level_is_processed_once()
    {
        await using var m = await TestMatch.StartAsync(factory);
        var caller = await CallerAsync(m, "Anna");
        var request = m.ShotAt("Cyril");

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<FireService>().FireAsync(caller, request);
        }));

        Assert.Single(results, r => !r.Duplicate);
        Assert.All(results, r => Assert.Equal(TargetOutcome.Eliminated, r.Targets.Single().Outcome));
        Assert.Equal(1, await factory.CountEliminationsAsync(m.SessionId));
        Assert.Equal(1, await factory.WithDbAsync(db => db.ShotRecords.CountAsync(r => r.SessionId == m.SessionId)));
        Assert.Equal(1, (await m.Score()).Version);
    }

    [Fact]
    public async Task Two_shooters_hitting_the_same_target_at_once_eliminate_it_exactly_once()
    {
        await using var m = await TestMatch.StartAsync(factory);

        var results = await Task.WhenAll(
            m.Fire("Anna", m.ShotAt("Cyril")),
            m.Fire("Bob", m.ShotAt("Cyril")));

        var outcomes = results.Select(r => r.Targets.Single()).ToList();
        Assert.Single(outcomes, t => t.Outcome == TargetOutcome.Eliminated);
        var loser = Assert.Single(outcomes, t => t.Outcome == TargetOutcome.Rejected);
        Assert.Equal(ErrorCodes.TargetNotAlive, loser.Reason);

        Assert.Equal(1, await factory.CountEliminationsAsync(m.SessionId, m.Id("Cyril")));
        Assert.Equal(1, (await m.Score()).Of("blue"));
    }

    [Fact]
    public async Task Two_shooters_race_many_times_at_service_level_without_double_elimination()
    {
        await using var m = await TestMatch.StartAsync(factory);
        var anna = await CallerAsync(m, "Anna");
        var bob = await CallerAsync(m, "Bob");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).SelectMany(_ => new[] { anna, bob }).Select(async caller =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<FireService>().FireAsync(caller, m.ShotAt("Cyril"));
        }));

        Assert.Equal(1, results.Count(r => r.Accepted));
        Assert.Equal(1, await factory.CountEliminationsAsync(m.SessionId));
        Assert.Equal(1, (await m.Score()).Of("blue"));
    }

    [Fact]
    public async Task Rules_own_marker_foreign_marker_spawn_marker_self_and_unknown_player()
    {
        await using var m = await TestMatch.StartAsync(factory);

        var result = await m.Fire("Anna", m.Shot(
            ("Cyril", [m.Marker("Cyril")]),   // own marker → hit
            ("Dana", [m.Marker("Cyril")]),    // somebody else's marker
            ("Anna", [m.Marker("Anna")]),     // self
            ("player_does_not_exist", [5])));

        Assert.True(result.Accepted);
        Assert.Equal(TargetOutcome.Eliminated, Outcome(result, m.Id("Cyril")).Outcome);
        Assert.Equal(ErrorCodes.MarkerMismatch, Outcome(result, m.Id("Dana")).Reason);
        Assert.Equal(ErrorCodes.TargetIsSelf, Outcome(result, m.Id("Anna")).Reason);
        Assert.Equal(ErrorCodes.TargetNotFound, Outcome(result, "player_does_not_exist").Reason);

        var spawn = await m.Fire("Anna", m.Shot(("Dana", [41])));
        Assert.False(spawn.Accepted);
        Assert.Equal(ErrorCodes.MarkerIsSpawnPoint, spawn.Targets.Single().Reason);

        Assert.Equal(PlayerStatus.Alive, (await m.PlayerState("Dana")).Status);
        Assert.Equal(PlayerStatus.Alive, (await m.PlayerState("Anna")).Status);
        Assert.Equal(1, (await m.Score()).Of("blue"));
    }

    [Fact]
    public async Task Duplicate_entries_of_one_player_are_merged_into_one_target()
    {
        await using var m = await TestMatch.StartAsync(factory);

        var result = await m.Fire("Anna", m.Shot(("Cyril", [m.Marker("Cyril")]), ("Cyril", [m.Marker("Cyril")])));

        Assert.Equal(TargetOutcome.Eliminated, Assert.Single(result.Targets).Outcome);
        Assert.Equal(1, await factory.CountEliminationsAsync(m.SessionId));
    }

    [Fact]
    public async Task Teammate_hit_is_rejected_when_friendly_fire_is_off()
    {
        await using var m = await TestMatch.StartAsync(factory);

        var result = await m.Fire("Anna", m.ShotAt("Bob"));

        Assert.False(result.Accepted);
        Assert.Equal(ErrorCodes.SameTeam, result.Targets.Single().Reason);
        Assert.Equal(PlayerStatus.Alive, (await m.PlayerState("Bob")).Status);
        Assert.Equal(0, (await m.Score()).Of("blue"));
    }

    [Fact]
    public async Task Teammate_hit_eliminates_but_does_not_change_score_when_friendly_fire_is_on()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { FriendlyFire = true });

        var result = await m.Fire("Anna", m.ShotAt("Bob"));

        Assert.True(result.Accepted);
        Assert.Equal(TargetOutcome.Eliminated, result.Targets.Single().Outcome);
        Assert.Equal(PlayerStatus.Respawning, (await m.PlayerState("Bob")).Status);
        var score = await m.Score();
        Assert.Equal((0, 0, 0), (score.Of("blue"), score.Of("red"), score.Version));

        var elimination = await factory.WithDbAsync(db => db.Eliminations.SingleAsync(e => e.SessionId == m.SessionId));
        Assert.True(elimination.FriendlyFire);
        Assert.False(elimination.CountsForScore);
        var hit = await m["Bob"].WaitFor<PlayerHitEvent>("PlayerHit");
        Assert.Equal(m.Id("Anna"), hit.ByPlayerId);
        Assert.Equal(0, m["Anna"].Count("ScoreUpdated"));
    }

    [Fact]
    public async Task Protected_target_cannot_be_hit_but_a_protected_shooter_can_fire()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { RespawnSeconds = 0, SpawnProtectionSeconds = 30 });

        Assert.True((await m.Fire("Anna", m.ShotAt("Cyril"))).Accepted);
        await m["Cyril"].WaitFor<PlayerStateEvent>("PlayerStateChanged", e => e.Status == PlayerStatus.Protected);
        Assert.Equal(PlayerStatus.Protected, (await m.PlayerState("Cyril")).Status);

        var onProtected = await m.Fire("Bob", m.ShotAt("Cyril"));
        Assert.False(onProtected.Accepted);
        Assert.Equal(ErrorCodes.TargetNotAlive, onProtected.Targets.Single().Reason);

        var byProtected = await m.Fire("Cyril", m.ShotAt("Bob"));
        Assert.True(byProtected.Accepted);
        Assert.Equal(1, (await m.Score()).Of("red"));
    }

    [Fact]
    public async Task Respawning_shooter_is_rejected_for_the_whole_request()
    {
        await using var m = await TestMatch.StartAsync(factory);
        Assert.True((await m.Fire("Anna", m.ShotAt("Cyril"))).Accepted);

        var result = await m.Fire("Cyril", m.ShotAt("Anna"));

        Assert.False(result.Accepted);
        Assert.Equal(ErrorCodes.ShooterNotAlive, result.ShotRejectedReason);
        Assert.Empty(result.Targets);
        Assert.Equal(PlayerStatus.Alive, (await m.PlayerState("Anna")).Status);
    }

    [Fact]
    public async Task Retry_returns_the_original_result_even_after_the_shooter_was_eliminated()
    {
        await using var m = await TestMatch.StartAsync(factory);
        var request = m.ShotAt("Cyril");
        var original = await m.Fire("Anna", request);
        Assert.True((await m.Fire("Dana", m.ShotAt("Anna"))).Accepted);

        var retry = await m.Fire("Anna", request);

        Assert.True(retry.Duplicate);
        Assert.True(retry.Accepted);
        Assert.Equal(original.Targets.Single().RespawnAt, retry.Targets.Single().RespawnAt);
    }

    [Fact]
    public async Task Fire_outside_a_running_match_is_rejected_with_SESSION_NOT_ACTIVE()
    {
        await using var lobby = await TestMatch.StartAsync(factory, start: false);
        var inLobby = await lobby.Fire("Anna", lobby.ShotAt("Cyril"));
        Assert.Equal(ErrorCodes.SessionNotActive, inLobby.ShotRejectedReason);
        Assert.False(inLobby.Accepted);

        await using var m = await TestMatch.StartAsync(factory);
        await m.Admin.Invoke("EndGame");
        var afterEnd = await m.Fire("Anna", m.ShotAt("Cyril"));
        Assert.Equal(ErrorCodes.SessionNotActive, afterEnd.ShotRejectedReason);
        Assert.Equal(PlayerStatus.Alive, (await m.PlayerState("Cyril")).Status);
    }

    [Fact]
    public async Task Request_level_validation_targets_rate_limit_and_timestamp()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { MaxTargetsPerShot = 1 });

        var tooMany = await m.Fire("Anna", m.ShotAt("Cyril", "Dana"));
        Assert.Equal(ErrorCodes.InvalidArgument, tooMany.ShotRejectedReason);

        var none = await m.Fire("Anna", m.Shot());
        Assert.Equal(ErrorCodes.InvalidArgument, none.ShotRejectedReason);

        var stale = m.ShotAt("Cyril") with { Timestamp = DateTime.UtcNow.AddSeconds(-10) };
        Assert.Equal(ErrorCodes.TimestampOutOfRange, (await m.Fire("Anna", stale)).ShotRejectedReason);

        var mismatch = await Assert.ThrowsAsync<HubException>(() => m.Fire("Anna", m.ShotAt("Cyril") with { SessionId = "game_other" }));
        Assert.Contains(ErrorCodes.SessionMismatch, mismatch.Message);

        var adminNotLinked = await Assert.ThrowsAsync<HubException>(() => m.Admin.Invoke<ShotResult>("Fire", m.ShotAt("Cyril")));
        Assert.Contains(ErrorCodes.Forbidden, adminNotLinked.Message);

        Assert.Equal(0, await factory.CountEliminationsAsync(m.SessionId));
    }

    [Fact]
    public async Task Rate_limit_rejects_a_second_shot_inside_the_minimum_interval()
    {
        await using var app = factory.WithWebHostBuilder(b => b.UseSetting("FireRules:MinShotIntervalMs", "10000"));
        await using var scope = app.Services.CreateAsyncScope();
        await using var m = await TestMatch.StartAsync(factory);
        var fire = scope.ServiceProvider.GetRequiredService<FireService>();
        var anna = await CallerAsync(m, "Anna");

        var first = await fire.FireAsync(anna, m.ShotAt("Cyril"));
        var second = await fire.FireAsync(anna, m.ShotAt("Dana"));

        Assert.True(first.Accepted);
        Assert.Equal(ErrorCodes.RateLimited, second.ShotRejectedReason);
        Assert.Equal(PlayerStatus.Alive, (await m.PlayerState("Dana")).Status);
    }

    private static TargetResult Outcome(ShotResult result, string playerId) => result.Targets.Single(t => t.PlayerId == playerId);

    private async Task<CallerIdentity> CallerAsync(TestMatch m, string nickname)
    {
        var playerId = m.Id(nickname);
        var link = await factory.WithDbAsync(db => db.Players.Where(p => p.Id == playerId).Select(p => p.DeviceLinkId).SingleAsync());
        return new CallerIdentity(playerId, m.SessionId, Roles.Player, playerId, link);
    }

    private Task SeedScoredEliminationsAsync(TestMatch m, string shooter, string target, int count) =>
        factory.WithDbAsync(async db =>
        {
            var shooterTeam = m.Registrations[shooter].TeamId;
            var targetTeam = m.Registrations[target].TeamId;
            for (var i = 0; i < count; i++)
            {
                db.Eliminations.Add(new Elimination
                {
                    SessionId = m.SessionId,
                    ShotId = $"seed_{Guid.NewGuid():N}",
                    ShooterId = m.Id(shooter),
                    ShooterTeamId = shooterTeam,
                    TargetId = m.Id(target),
                    TargetTeamId = targetTeam,
                    OccurredAt = DateTime.UtcNow.AddMinutes(-10),
                    CountsForScore = true,
                });
            }

            return await db.SaveChangesAsync();
        });
}
