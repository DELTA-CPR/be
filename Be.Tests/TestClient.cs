using System.Collections.Concurrent;
using System.Text.Json;
using Be.Contracts;
using Be.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Be.Tests;

/// <summary>A connected phone: a hub connection that records every server → client event.</summary>
public sealed class TestClient : IAsyncDisposable
{
    private static readonly string[] Events =
    [
        "GameConfigUpdated", "RosterUpdated", "GameStarted", "GameEnded", "PlayerHit",
        "PlayerEliminated", "PlayerRespawned", "PlayerStateChanged", "ScoreUpdated", "Kicked",
    ];

    private readonly ConcurrentQueue<(string Name, JsonElement Payload)> _received = new();
    private readonly SemaphoreSlim _signal = new(0);

    public HubConnection Connection { get; }
    public string Name { get; }
    public string Token { get; }
    public string? PlayerId { get; }

    public TestClient(GameFactory factory, string name, string token, string? playerId)
    {
        Name = name;
        Token = token;
        PlayerId = playerId;
        var server = factory.Server;
        Connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/game"), o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.HttpMessageHandlerFactory = _ => server.CreateHandler();
                o.WebSocketFactory = (ctx, ct) =>
                {
                    var ws = server.CreateWebSocketClient();
                    ws.ConfigureRequest = r => r.Headers.Authorization = $"Bearer {token}";
                    return new ValueTask<System.Net.WebSockets.WebSocket>(ws.ConnectAsync(ctx.Uri, ct));
                };
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .AddJsonProtocol(o => GameJson.Configure(o.PayloadSerializerOptions))
            .Build();

        foreach (var evt in Events)
        {
            Connection.On<JsonElement>(evt, payload =>
            {
                _received.Enqueue((evt, payload.Clone()));
                _signal.Release();
            });
        }
    }

    public Task StartAsync() => Connection.StartAsync();

    public Task StopAsync() => Connection.StopAsync();

    public Task<T> Invoke<T>(string method, params object?[] args) => Connection.InvokeCoreAsync<T>(method, args);

    public Task Invoke(string method, params object?[] args) => Connection.InvokeCoreAsync(method, args);

    public IReadOnlyList<T> All<T>(string eventName) =>
        _received.Where(r => r.Name == eventName).Select(r => r.Payload.Deserialize<T>(GameJson.Options)!).ToList();

    public int Count(string eventName) => _received.Count(r => r.Name == eventName);

    /// <summary>Waits until an event matching <paramref name="predicate"/> has arrived.</summary>
    public async Task<T> WaitFor<T>(string eventName, Func<T, bool>? predicate = null, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            var match = All<T>(eventName).FirstOrDefault(e => predicate?.Invoke(e) ?? true);
            if (match is not null)
            {
                return match;
            }

            try
            {
                await _signal.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"{Name} did not receive {eventName} within {timeoutMs} ms.");
            }
        }
    }

    public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
}
