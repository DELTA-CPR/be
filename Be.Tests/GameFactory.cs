using Be.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Be.Tests.GameFactory))]

namespace Be.Tests;

/// <summary>
/// Runs the real API against a throwaway PostgreSQL. Uses Testcontainers (Docker) unless
/// <c>TEST_DB_CONNECTION</c> points at an existing server, in which case a fresh database is created there.
/// </summary>
public sealed class GameFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _connectionString = "";

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");
        if (!string.IsNullOrEmpty(external))
        {
            _connectionString = $"{external.TrimEnd(';')};Database=be_test_{Guid.NewGuid():N}";
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }

        // Forces host start-up, which applies the migrations.
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:GameDb", _connectionString);
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-test-signing-key-0123456789");
        // Tests fire many shots back to back; the rate limit has its own test with a dedicated value.
        builder.UseSetting("FireRules:MinShotIntervalMs", "0");
        builder.UseSetting("RespawnSweeper:IntervalMs", "100");
    }

    public async Task<T> WithDbAsync<T>(Func<GameDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<GameDbContext>());
    }

    public Task<int> CountEliminationsAsync(string sessionId, string? targetId = null) =>
        WithDbAsync(db => db.Eliminations.CountAsync(e => e.SessionId == sessionId && (targetId == null || e.TargetId == targetId)));

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
