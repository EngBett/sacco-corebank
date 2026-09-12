using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sacco.Seed;

/// <summary>
/// Usage:
///   dotnet run --project backend/seed/Sacco.Seed              # migrate + seed (idempotent)
///   dotnet run --project backend/seed/Sacco.Seed -- --reset   # drop, recreate, migrate, seed
/// Connection: ConnectionStrings:Sacco in appsettings.json, or SACCO_CONNECTION / ConnectionStrings__Sacco env vars.
/// </summary>
public static class SeedCli
{
    public static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var reset = args.Contains("--reset", StringComparer.OrdinalIgnoreCase);
        var connection = Environment.GetEnvironmentVariable("SACCO_CONNECTION")
                         ?? config.GetConnectionString("Sacco")
                         ?? throw new InvalidOperationException("No connection string. Set SACCO_CONNECTION or ConnectionStrings:Sacco.");

        using var loggerFactory = LoggerFactory.Create(b => b.AddConfiguration(config.GetSection("Logging")).AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }).SetMinimumLevel(LogLevel.Information));

        try
        {
            await SeedRunner.RunAsync(connection, reset, loggerFactory);
            return 0;
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("Seed").LogError(ex, "Seeding failed");
            return 1;
        }
    }
}
