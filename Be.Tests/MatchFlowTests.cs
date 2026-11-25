using System.Net;
using System.Net.Http.Json;
using Be.Contracts;
using Be.Domain;
using Be.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace Be.Tests;

public sealed class MatchFlowTests(GameFactory factory)
{
    [Fact]
    public async Task Timer_respawn_restores_alive_and_notifies_everyone()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { RespawnSeconds = 1 });

        var result = await m.Fire("Anna", m.ShotAt("Cyril"));
        var respawned = await m["Cyril"].WaitFor<PlayerRespawnedEvent>("PlayerRespawned", timeoutMs: 5000);

        Assert.Equal(PlayerStatus.Alive, respawned.Status);
        Assert.True(respawned.At >= result.Targets.Single().RespawnAt!.Value.AddMilliseconds(-1));
        await m["Anna"].WaitFor<PlayerRespawnedEvent>("PlayerRespawned");
        Assert.Equal(PlayerStatus.Alive, (await m.PlayerState("Cyril")).Status);

        // Back in the game: can be hit again and scores again.
        Assert.True((await m.Fire("Bob", m.ShotAt("Cyril"))).Accepted);
        Assert.Equal(2, (await m.Score()).Of("blue"));
    }

    [Fact]
    public async Task Spawn_protection_ends_with_PlayerStateChanged_alive()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { RespawnSeconds = 0, SpawnProtectionSeconds = 1 });

        await m.Fire("Anna", m.ShotAt("Cyril"));
        var respawned = await m["Cyril"].WaitFor<PlayerRespawnedEvent>("PlayerRespawned");
        Assert.Equal(PlayerStatus.Protected, respawned.Status);
        Assert.NotNull(respawned.ProtectedUntil);

        await m["Cyril"].WaitFor<PlayerStateEvent>("PlayerStateChanged", e => e.Status == PlayerStatus.Alive, timeoutMs: 5000);
        Assert.Equal(PlayerStatus.Alive, (await m.PlayerState("Cyril")).Status);
    }

    [Fact]
    public async Task Qr_mode_requires_scanning_own_team_spawn_after_respawnAt()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { RespawnMode = RespawnMode.Qr, RespawnSeconds = 0 });

        await m.Fire("Anna", m.ShotAt("Cyril"));
        var hit = await m["Cyril"].WaitFor<PlayerHitEvent>("PlayerHit");
        Assert.Equal(RespawnMode.Qr, hit.RespawnMode);

        // The sweeper must not respawn QR-mode players on its own.
        await Task.Delay(400);
        Assert.Equal(PlayerStatus.Respawning, (await m.PlayerState("Cyril")).Status);

        var cyril = m["Cyril"];
        Assert.Equal(ErrorCodes.NotASpawnMarker, (await cyril.Invoke<ConfirmSpawnResult>("ConfirmSpawn", 11)).Reason);
        Assert.Equal(ErrorCodes.WrongTeamSpawn, (await cyril.Invoke<ConfirmSpawnResult>("ConfirmSpawn", 40)).Reason);

        var confirmed = await cyril.Invoke<ConfirmSpawnResult>("ConfirmSpawn", 41);
        Assert.True(confirmed.Confirmed);
        Assert.Equal(PlayerStatus.Alive, confirmed.State);
        await m["Anna"].WaitFor<PlayerRespawnedEvent>("PlayerRespawned");

        Assert.Equal(ErrorCodes.RespawnNotReady, (await cyril.Invoke<ConfirmSpawnResult>("ConfirmSpawn", 41)).Reason);
    }

    [Fact]
    public async Task Reconnect_after_missed_hit_and_respawn_sees_the_same_state_as_a_connected_client()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { RespawnSeconds = 1 });
        var dana = m["Dana"];
        await dana.StopAsync();

        await m.Fire("Anna", m.ShotAt("Cyril"));
        await m["Cyril"].WaitFor<PlayerRespawnedEvent>("PlayerRespawned", timeoutMs: 5000);
        await m.Fire("Bob", m.ShotAt("Cyril"));

        await dana.StartAsync();
        var reconnected = await m.State(dana);
        var connected = await m.State(m["Cyril"]);

        Assert.Equal(0, dana.Count("PlayerEliminated"));
        Assert.Equal(connected.Players, reconnected.Players);
        Assert.Equivalent(connected.Score, reconnected.Score, strict: true);
        Assert.Equal(2, reconnected.Score.Of("blue"));
        Assert.Equal(m.Id("Dana"), reconnected.Self!.PlayerId);
        Assert.Equal(PlayerStatus.Respawning, reconnected.Players.Single(p => p.PlayerId == m.Id("Cyril")).Status);
    }

    [Fact]
    public async Task Team_visibility_limits_live_events_and_snapshot_to_the_victims_team()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { EliminationVisibility = EliminationVisibility.Team });

        await m.Fire("Anna", m.ShotAt("Cyril"));

        await m["Dana"].WaitFor<EliminationEvent>("PlayerEliminated");
        await m.Admin.WaitFor<EliminationEvent>("PlayerEliminated");
        await m["Bob"].WaitFor<Scoreboard>("ScoreUpdated");
        await Task.Delay(200);
        Assert.Equal(0, m["Bob"].Count("PlayerEliminated"));
        Assert.Equal(0, m["Anna"].Count("PlayerEliminated"));
        Assert.Equal(1, m["Cyril"].Count("PlayerEliminated"));

        var blueView = await m.State(m["Bob"]);
        Assert.All(blueView.Players, p => Assert.Equal("blue", p.TeamId));
        var redView = await m.State(m["Dana"]);
        Assert.Contains(redView.Players, p => p.PlayerId == m.Id("Cyril") && p.Status == PlayerStatus.Respawning);
        Assert.Equal(4, (await m.State(m.Admin)).Players.Count);
    }

    [Fact]
    public async Task Victim_visibility_sends_elimination_only_to_the_victim_and_admin()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { EliminationVisibility = EliminationVisibility.Victim });

        await m.Fire("Anna", m.ShotAt("Cyril"));

        await m["Cyril"].WaitFor<EliminationEvent>("PlayerEliminated");
        await m.Admin.WaitFor<EliminationEvent>("PlayerEliminated");
        await Task.Delay(200);
        Assert.Equal(0, m["Dana"].Count("PlayerEliminated"));
        var danaView = await m.State(m["Dana"]);
        Assert.Equal(m.Id("Dana"), Assert.Single(danaView.Players).PlayerId);
    }

    [Fact]
    public async Task EndGame_broadcasts_final_table_and_later_shots_do_not_count()
    {
        await using var m = await TestMatch.StartAsync(factory);
        await m.Fire("Anna", m.ShotAt("Cyril"));
        await m.Fire("Dana", m.ShotAt("Bob"));
        await m.Fire("Anna", m.ShotAt("Dana"));

        await m.Admin.Invoke("EndGame");
        var ended = await m["Cyril"].WaitFor<GameEndedEvent>("GameEnded");

        Assert.Equal(GameEndReason.Admin, ended.Reason);
        Assert.Equal((2, 1), (ended.Score.Of("blue"), ended.Score.Of("red")));
        Assert.Equal(SessionStatus.Ended, (await m.State(m.Admin)).Status);
        await Assert.ThrowsAsync<HubException>(() => m.Admin.Invoke("EndGame"));
    }

    [Fact]
    public async Task EndGame_racing_shots_final_table_matches_committed_eliminations()
    {
        await using var m = await TestMatch.StartAsync(factory);

        var shots = new[] { m.Fire("Anna", m.ShotAt("Cyril")), m.Fire("Bob", m.ShotAt("Dana")), m.Fire("Cyril", m.ShotAt("Anna")) };
        var end = m.Admin.Invoke("EndGame");
        var results = await Task.WhenAll(shots);
        await end;

        var ended = await m.Admin.WaitFor<GameEndedEvent>("GameEnded");
        var accepted = results.Count(r => r.Accepted);
        Assert.Equal(accepted, ended.Score.Teams.Sum(t => t.Eliminations));
        Assert.Equal(accepted, await factory.CountEliminationsAsync(m.SessionId));
        Assert.All(results.Where(r => !r.Accepted), r => Assert.Contains(r.ShotRejectedReason ?? r.Targets.Single().Reason, new[] { ErrorCodes.SessionNotActive, ErrorCodes.ShooterNotAlive, ErrorCodes.TargetNotAlive }));
    }

    [Fact]
    public async Task Match_timeout_ends_the_game_with_the_same_GameEnded()
    {
        await using var m = await TestMatch.StartAsync(factory, c => c with { MatchDurationSeconds = 10 });
        await m.Fire("Anna", m.ShotAt("Cyril"));
        // Pull the deadline into the past instead of waiting ten seconds.
        await factory.WithDbAsync(db => db.Sessions.Where(s => s.Id == m.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndsAt, DateTime.UtcNow.AddSeconds(-1))));

        var ended = await m["Dana"].WaitFor<GameEndedEvent>("GameEnded", timeoutMs: 5000);

        Assert.Equal(GameEndReason.Timeout, ended.Reason);
        Assert.Equal(1, ended.Score.Of("blue"));
    }

    [Fact]
    public async Task Lobby_and_bootstrap_rules()
    {
        await using var m = await TestMatch.StartAsync(factory, start: false);

        var markerInUse = await Assert.ThrowsAsync<HubException>(() =>
            m.Admin.Invoke<PlayerRegistration>("RegisterPlayer", new RegisterPlayerRequest("Eve", "red", 40)));
        Assert.Contains(ErrorCodes.MarkerInUse, markerInUse.Message);
        var nickname = await Assert.ThrowsAsync<HubException>(() =>
            m.Admin.Invoke<PlayerRegistration>("RegisterPlayer", new RegisterPlayerRequest("Anna", "red", 20)));
        Assert.Contains(ErrorCodes.NicknameTaken, nickname.Message);
        var notAdmin = await Assert.ThrowsAsync<HubException>(() => m["Anna"].Invoke("StartGame"));
        Assert.Contains(ErrorCodes.Forbidden, notAdmin.Message);

        var table = await m["Anna"].Invoke<MappingTable>("GetMappingTable");
        Assert.Equal(4, table.Markers.Count(x => x.Kind == MarkerKind.Player));
        Assert.Equal(2, table.Markers.Count(x => x.Kind == MarkerKind.Spawn));

        // Device linking: an already linked player and a spawn marker are refused.
        var code = await factory.WithDbAsync(db => db.Sessions.Where(s => s.Id == m.SessionId).Select(s => s.Code).SingleAsync());
        var http = factory.CreateClient();
        var joined = await (await http.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(code), GameJson.Options))
            .Content.ReadFromJsonAsync<JoinSessionResponse>(GameJson.Options);
        http.DefaultRequestHeaders.Authorization = new("Bearer", joined!.AccessToken);
        var again = await http.PostAsJsonAsync($"/api/sessions/{m.SessionId}/device-link", new DeviceLinkRequest(m.Marker("Anna")), GameJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains(ErrorCodes.PlayerAlreadyLinked, await again.Content.ReadAsStringAsync());
        var spawn = await http.PostAsJsonAsync($"/api/sessions/{m.SessionId}/device-link", new DeviceLinkRequest(40), GameJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, spawn.StatusCode);
        var unknown = await http.PostAsJsonAsync($"/api/sessions/{m.SessionId}/device-link", new DeviceLinkRequest(33), GameJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var otherSession = await http.PostAsJsonAsync("/api/sessions/game_other/device-link", new DeviceLinkRequest(1), GameJson.Options);
        Assert.Equal(HttpStatusCode.Forbidden, otherSession.StatusCode);

        // A pending token cannot open the hub.
        var pending = new TestClient(factory, "pending", joined.AccessToken, null);
        await Assert.ThrowsAnyAsync<Exception>(() => pending.StartAsync());
        await pending.DisposeAsync();

        await m.Admin.Invoke("StartGame");
        Assert.Equal(SessionStatus.Running, (await m.State(m.Admin)).Status);
        var lateRegistration = await Assert.ThrowsAsync<HubException>(() =>
            m.Admin.Invoke<PlayerRegistration>("RegisterPlayer", new RegisterPlayerRequest("Eve", "red", 20)));
        Assert.Contains(ErrorCodes.WrongPhase, lateRegistration.Message);
    }

    [Fact]
    public async Task Admin_can_link_own_marker_and_play()
    {
        await using var m = await TestMatch.StartAsync(factory, start: false, players:
        [
            new("Anna", "blue", 1),
            new("Cyril", "red", 11),
            new("Admin", "blue", 3),
        ]);
        // The test helper linked "Admin" with a pending token; release it and link with the admin token instead.
        await m.Admin.Invoke("UpdatePlayer", new UpdatePlayerRequest(m.Id("Admin"), null, null, ReleaseDevice: true));
        await m["Admin"].WaitFor<string>("Kicked");

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", m.Admin.Token);
        var linked = await (await http.PostAsJsonAsync($"/api/sessions/{m.SessionId}/device-link", new DeviceLinkRequest(3), GameJson.Options))
            .EnsureOk().Content.ReadFromJsonAsync<DeviceLinkResponse>(GameJson.Options);
        Assert.Equal(Roles.Admin, linked!.Role);

        await using var adminPlayer = new TestClient(factory, "admin-player", linked.UpgradedToken, linked.PlayerId);
        await adminPlayer.StartAsync();
        await adminPlayer.Invoke("StartGame");
        var result = await adminPlayer.Invoke<ShotResult>("Fire", m.ShotAt("Cyril"));
        Assert.True(result.Accepted);
        await adminPlayer.WaitFor<GameStartedEvent>("GameStarted");
    }
}
