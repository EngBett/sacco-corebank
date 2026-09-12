using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sacco.Shared.Tenancy;

namespace Sacco.Shared.Persistence;

public static class DbContextRegistration
{
    public const string MigrationsAssembly = "Sacco.Migrations";
    public const string HistoryTable = "__ef_migrations_history";

    /// <summary>Registers a module DbContext with the shared conventions: snake_case, module schema, tenant interceptor.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string connectionString, string schema)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>((sp, options) =>
        {
            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly(MigrationsAssembly);
                    npgsql.MigrationsHistoryTable(HistoryTable, schema);
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery); // aggregates auto-include several collections
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantConnectionInterceptor(sp.GetRequiredService<ITenantContext>()));
        });
        return services;
    }
}
