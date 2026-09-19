using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sacco.Modules.Identity;
using Sacco.Modules.Identity.IdentityServer;
using Sacco.Modules.Identity.Persistence;
using Sacco.Modules.Ledger;
using Sacco.Modules.Members;
using Sacco.Modules.Members.Persistence;
using Sacco.Modules.Notifications;
using Sacco.Modules.Notifications.Persistence;
using Sacco.Modules.Savings;
using Sacco.Modules.Savings.Persistence;
using Sacco.Modules.Lending;
using Sacco.Modules.Lending.Persistence;
using Sacco.Modules.Payments;
using Sacco.Modules.Payments.Persistence;
using Sacco.Modules.Reporting;
using Sacco.Modules.Reporting.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Modules.Platform;
using Sacco.Modules.Platform.Persistence;
using Sacco.Seed.Seeders;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;
using Microsoft.Extensions.Configuration;

namespace Sacco.Seed;

/// <summary>
/// Entry point shared by the CLI and the integration tests: migrates every module schema, then
/// runs every <see cref="ISeeder"/> in order. Safe to re-run.
/// </summary>
public static class SeedRunner
{
    public static async Task RunAsync(string connectionString, bool reset, ILoggerFactory loggerFactory, CancellationToken ct = default)
    {
        var logger = loggerFactory.CreateLogger("Seed");

        if (reset)
        {
            await DropDatabaseAsync(connectionString, logger, ct);
        }

        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddSingleton<IClock, SeedClock>();
        services.AddPlatformModule(services.BuildServiceProvider().GetRequiredService<IConfiguration>(), connectionString);
        services.AddLedgerModule(connectionString);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IdentityServer:IssuerUri"] = "http://seed.local" }).Build();
        services.AddScoped<ITenantLookup, SeedTenantLookup>();
        services.AddIdentityModule(config, new HostingEnvironment { EnvironmentName = Environments.Development }, connectionString);
        services.AddMembersModule(config, connectionString);
        services.AddSavingsModule(config, connectionString);
        services.AddLendingModule(config, connectionString);
        services.AddPaymentsModule(config, connectionString);
        services.AddReportingModule(config, connectionString);
        services.AddNotificationsModule(config, connectionString); // no realtime pusher: nobody is connected during seeding; sandbox SMS/email land in the outbox
        services.AddSingleton<Wolverine.IMessageBus, SeedMessageBusStub>();

        services.AddScoped<ISeeder, TenantSeeder>();
        services.AddScoped<ISeeder, ChartOfAccountsSeeder>();
        services.AddScoped<ISeeder, IdentitySeeder>();
        services.AddScoped<ISeeder, MembersSeeder>();
        services.AddScoped<ISeeder, SavingsSeeder>();
        services.AddScoped<ISeeder, LedgerSeeder>();
        services.AddScoped<ISeeder, SavingsScenarioSeeder>();
        services.AddScoped<ISeeder, LendingSeeder>();
        services.AddScoped<ISeeder, PaymentsSeeder>();
        services.AddScoped<ISeeder, ReportingSeeder>();
        services.AddScoped<ISeeder, NotificationsSeeder>();

        await using var provider = services.BuildServiceProvider();

        await EnsureDatabaseAsync(connectionString, logger, ct);

        using (var scope = provider.CreateScope())
        {
            logger.LogInformation("Applying migrations");
            await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<MembersDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<SavingsDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<LendingDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(ct);
            // Defence in depth: the policies now bind the table owner too (the role the API and this tool run as).
            await Sacco.Shared.Persistence.RowLevelSecurity.ForceTenantIsolationAsync(scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.GetDbConnection(), ct);
        }

        using (var scope = provider.CreateScope())
        {
            var seeders = scope.ServiceProvider.GetServices<ISeeder>().OrderBy(s => s.Order).ToList();
            foreach (var seeder in seeders)
            {
                logger.LogInformation("Running {Seeder}", seeder.GetType().Name);
                await seeder.SeedAsync(ct);
            }
        }

        logger.LogInformation("Seed complete");
    }

    private static async Task EnsureDatabaseAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        var dbName = csb.Database ?? throw new InvalidOperationException("Connection string has no database.");
        csb.Database = "postgres";
        await using var conn = new NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        await using var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conn);
        check.Parameters.AddWithValue("n", dbName);
        if (await check.ExecuteScalarAsync(ct) is null)
        {
            logger.LogInformation("Creating database {Db}", dbName);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
            await create.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task DropDatabaseAsync(string connectionString, ILogger logger, CancellationToken ct)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        var dbName = csb.Database!;
        csb.Database = "postgres";
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync(ct);
        logger.LogWarning("Dropping database {Db}", dbName);
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
        await drop.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>The seeder only ever targets the demo tenant.</summary>
file sealed class SeedTenantLookup : ITenantLookup
{
    public Task<(Guid Id, string Slug, string Name, string PrimaryColor, string? LogoUrl, string? FaviconUrl)?> FindBySlugAsync(string slug, CancellationToken ct)
        => Task.FromResult<(Guid, string, string, string, string?, string?)?>(slug == Data.DemoTenant.Slug
            ? (Data.DemoTenant.Id, Data.DemoTenant.Slug, Data.DemoTenant.Name, Data.DemoTenant.PrimaryColor, Data.DemoTenant.LogoUrl, Data.DemoTenant.FaviconUrl)
            : null);
}
