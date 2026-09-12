using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sacco.Modules.Savings.Application;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Modules.Savings.Persistence;
using Sacco.Shared.Http;
using Sacco.Shared.Persistence;
using Sacco.Shared.Savings;

namespace Sacco.Modules.Savings;

public static class SavingsModule
{
    public static IServiceCollection AddSavingsModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddModuleDbContext<SavingsDbContext>(connectionString, SavingsDbContext.SchemaName);
        services.Configure<SavingsSettings>(configuration.GetSection(SavingsSettings.SectionName));
        services.AddScoped<SavingsService>();
        services.AddScoped<ISavingsService>(sp => sp.GetRequiredService<SavingsService>());
        services.AddScoped<DividendService>();
        services.AddSingleton<IModuleEndpoints, SavingsEndpoints>();
        return services;
    }
}
