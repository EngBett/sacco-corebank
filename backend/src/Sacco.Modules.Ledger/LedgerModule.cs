using Microsoft.Extensions.DependencyInjection;
using Sacco.Modules.Ledger.Application;
using Sacco.Modules.Ledger.Endpoints;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Shared.Http;
using Sacco.Shared.Ledger;
using Sacco.Shared.Persistence;

namespace Sacco.Modules.Ledger;

public static class LedgerModule
{
    public static IServiceCollection AddLedgerModule(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<LedgerDbContext>(connectionString, LedgerDbContext.SchemaName);
        services.AddScoped<PostingEngine>();
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<JournalWorkflow>();
        services.AddScoped<LedgerQueries>();
        services.AddSingleton<IModuleEndpoints, LedgerEndpoints>();
        return services;
    }
}
