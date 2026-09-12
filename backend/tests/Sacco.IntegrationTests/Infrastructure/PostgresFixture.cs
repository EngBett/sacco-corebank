using Microsoft.Extensions.Logging;
using Sacco.Seed;
using Testcontainers.PostgreSql;

namespace Sacco.IntegrationTests.Infrastructure;

/// <summary>
/// One real Postgres per test run (Testcontainers), migrated and seeded with the Demo SACCO
/// exactly as a reviewer would do locally. Set SACCO_TEST_CONNECTION to reuse an existing server.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("SACCO_TEST_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("sacco_test").WithUsername("sacco").WithPassword("sacco").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        using var lf = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        await SeedRunner.RunAsync(ConnectionString, reset: false, lf);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "database";
}
