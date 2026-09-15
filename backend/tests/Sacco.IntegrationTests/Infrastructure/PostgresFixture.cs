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
        ConnectionString = await CreateAppRoleAsync(ConnectionString);
    }

    /// <summary>
    /// The container's user is a superuser, which bypasses row-level security whatever the policies say. The API under
    /// test therefore connects as a plain role with table privileges only — the same shape as production, where the
    /// application user is not a superuser and forced RLS binds it (ADR 0011).
    /// </summary>
    private static async Task<string> CreateAppRoleAsync(string adminConnectionString)
    {
        const string role = "sacco_app";
        await using var conn = new Npgsql.NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync();
        var schemas = new List<string>();
        await using (var q = new Npgsql.NpgsqlCommand("SELECT nspname FROM pg_namespace WHERE nspname NOT IN ('pg_catalog','information_schema','pg_toast') AND nspname NOT LIKE 'pg_temp%'", conn))
        await using (var r = await q.ExecuteReaderAsync())
            while (await r.ReadAsync()) schemas.Add(r.GetString(0));
        var sql = new System.Text.StringBuilder();
        sql.AppendLine($"DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN CREATE ROLE {role} LOGIN PASSWORD '{role}' NOSUPERUSER NOBYPASSRLS; END IF; END $$;");
        sql.AppendLine($"GRANT CONNECT, CREATE ON DATABASE \"{conn.Database}\" TO {role};");
        foreach (var schema in schemas)
        {
            sql.AppendLine($"GRANT USAGE ON SCHEMA \"{schema}\" TO {role};");
            sql.AppendLine($"GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA \"{schema}\" TO {role};");
            sql.AppendLine($"GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA \"{schema}\" TO {role};");
            sql.AppendLine($"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{schema}\" GRANT ALL PRIVILEGES ON TABLES TO {role};");
            sql.AppendLine($"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{schema}\" GRANT ALL PRIVILEGES ON SEQUENCES TO {role};");
        }
        await using (var cmd = new Npgsql.NpgsqlCommand(sql.ToString(), conn)) await cmd.ExecuteNonQueryAsync();
        return new Npgsql.NpgsqlConnectionStringBuilder(adminConnectionString) { Username = role, Password = role }.ConnectionString;
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
