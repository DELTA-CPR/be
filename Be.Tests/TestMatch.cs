using System.Net.Http.Json;
using Be.Contracts;
using Be.Domain;
using Be.Infrastructure;

namespace Be.Tests;

public sealed record PlayerSpec(string Nickname, string TeamId, int MarkerId);

/// <summary>A running session with an admin and linked, connected player phones.</summary>
public sealed class TestMatch : IAsyncDisposable
{
    private readonly GameFactory _factory;
    private readonly List<TestClient> _clients = [];
    private int _shotCounter;

    public required string SessionId { get; init; }
    public required TestClient Admin { get; init; }
    public Dictionary<string, TestClient> Players { get; } = new();
    public Dictionary<string, PlayerRegistration> Registrations { get; } = new();

    private TestMatch(GameFactory factory) => _factory = factory;

    public TestClient this[string nickname] => Players[nickname];

    public string Id(string nickname) => Registrations[nickname].PlayerId;

    public int Marker(string nickname) => Registrations[nickname].MarkerId;

    /// <summary>Blue: Anna (1), Bob (2). Red: Cyril (11), Dana (12). Spawn markers 40 (blue) and 41 (red).</summary>
    public static readonly PlayerSpec[] DefaultPlayers =
    [
        new("Anna", "blue", 1),
        new("Bob", "blue", 2),
        new("Cyril", "red", 11),
        new("Dana", "red", 12),
    ];

    public static async Task<TestMatch> StartAsync(
        GameFactory factory,
        Func<GameConfig, GameConfig>? configure = null,
        PlayerSpec[]? players = null,
        bool start = true)
    {
        var http = factory.CreateClient();
        var created = await (await http.PostAsJsonAsync("/api/sessions", new { }, GameJson.Options)).EnsureOk()
            .Content.ReadFromJsonAsync<CreateSessionResponse>(GameJson.Options);

        var admin = new TestClient(factory, "admin", created!.AdminToken, null);
        await admin.StartAsync();
        var match = new TestMatch(factory) { SessionId = created.SessionId, Admin = admin };
        match._clients.Add(admin);

        var config = configure?.Invoke(DefaultConfig) ?? DefaultConfig;
        await admin.Invoke("UpdateGameConfig", config);

        foreach (var p in players ?? DefaultPlayers)
        {
            match.Registrations[p.Nickname] = await admin.Invoke<PlayerRegistration>(
                "RegisterPlayer", new RegisterPlayerRequest(p.Nickname, p.TeamId, p.MarkerId));
        }

        await admin.Invoke<SpawnPointInfo>("RegisterSpawnPoint", new RegisterSpawnPointRequest(40, "Blue base", "blue"));
        await admin.Invoke<SpawnPointInfo>("RegisterSpawnPoint", new RegisterSpawnPointRequest(41, "Red base", "red"));

        foreach (var (nickname, registration) in match.Registrations)
        {
            var token = await LinkDeviceAsync(factory, created.SessionCode, created.SessionId, registration.MarkerId);
            var client = new TestClient(factory, nickname, token, registration.PlayerId);
            await client.StartAsync();
            match.Players[nickname] = client;
            match._clients.Add(client);
        }

        if (start)
        {
            await admin.Invoke("StartGame");
        }

        return match;
    }

    public static GameConfig DefaultConfig => new()
    {
        CrosshairSize = 0.12,
        FireWindowMs = 150,
        RespawnSeconds = 300,
        MatchDurationSeconds = 1800,
        MaxTargetsPerShot = 8,
        RespawnMode = RespawnMode.Timer,
        SpawnProtectionSeconds = 0,
        EliminationVisibility = EliminationVisibility.All,
        FriendlyFire = false,
    };

    public static async Task<string> LinkDeviceAsync(GameFactory factory, string code, string sessionId, int markerId)
    {
        var http = factory.CreateClient();
        var joined = await (await http.PostAsJsonAsync("/api/sessions/join", new JoinSessionRequest(code), GameJson.Options))
            .EnsureOk().Content.ReadFromJsonAsync<JoinSessionResponse>(GameJson.Options);
        http.DefaultRequestHeaders.Authorization = new("Bearer", joined!.AccessToken);
        var linked = await (await http.PostAsJsonAsync($"/api/sessions/{sessionId}/device-link", new DeviceLinkRequest(markerId), GameJson.Options))
            .EnsureOk().Content.ReadFromJsonAsync<DeviceLinkResponse>(GameJson.Options);
        return linked!.UpgradedToken;
    }

    /// <summary>A second connection for the same phone (for example the new connection after a reconnect).</summary>
    public async Task<TestClient> ConnectAgainAsync(string nickname)
    {
        var original = Players[nickname];
        var client = new TestClient(_factory, nickname + "#2", original.Token, original.PlayerId);
        await client.StartAsync();
        _clients.Add(client);
        return client;
    }

    public ShotRequest Shot(params (string Nickname, int[] MarkerIds)[] targets) => ShotWithId(NextShotId(), targets);

    public ShotRequest ShotWithId(string shotId, params (string Nickname, int[] MarkerIds)[] targets) => new()
    {
        SessionId = SessionId,
        ShotId = shotId,
        Timestamp = DateTime.UtcNow,
        Targets = targets.Select(t => new ShotTarget(Registrations.TryGetValue(t.Nickname, out var r) ? r.PlayerId : t.Nickname, t.MarkerIds)).ToList(),
    };

    public string NextShotId() => $"shot_{Interlocked.Increment(ref _shotCounter):D6}_{Guid.NewGuid():N}";

    /// <summary>Shot at the target's own registered marker.</summary>
    public ShotRequest ShotAt(params string[] nicknames) => Shot(nicknames.Select(n => (n, new[] { Marker(n) })).ToArray());

    public Task<ShotResult> Fire(string shooter, ShotRequest request) => Players[shooter].Invoke<ShotResult>("Fire", request);

    public Task<GameStateSnapshot> State(TestClient client) => client.Invoke<GameStateSnapshot>("GetGameState");

    public async Task<Scoreboard> Score() => (await State(Admin)).Score;

    public async Task<PlayerState> PlayerState(string nickname) =>
        (await State(Admin)).Players.Single(p => p.PlayerId == Id(nickname));

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients)
        {
            await c.DisposeAsync();
        }
    }
}

public static class ScoreboardExtensions
{
    public static int Of(this Scoreboard score, string teamId) => score.Teams.Single(t => t.TeamId == teamId).Eliminations;
}

public static class HttpAssertions
{
    /// <summary>Like EnsureSuccessStatusCode, but keeps the ProblemDetails body in the failure message.</summary>
    public static HttpResponseMessage EnsureOk(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new HttpRequestException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {body}");
        }

        return response;
    }
}
