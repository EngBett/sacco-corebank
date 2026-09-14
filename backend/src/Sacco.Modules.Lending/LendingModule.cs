using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sacco.Modules.Lending.Application;
using Sacco.Modules.Lending.Endpoints;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Http;
using Sacco.Shared.Lending;
using Sacco.Shared.Persistence;

namespace Sacco.Modules.Lending;

public static class LendingModule
{
    public static IServiceCollection AddLendingModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddModuleDbContext<LendingDbContext>(connectionString, LendingDbContext.SchemaName);
        services.Configure<LendingSettings>(configuration.GetSection(LendingSettings.SectionName));
        services.AddScoped<LoanService>();
        services.AddScoped<RepaymentService>();
        services.AddScoped<ILendingService>(sp => sp.GetRequiredService<RepaymentService>());
        services.AddScoped<ProvisioningService>();
        services.AddScoped<CreditScoringService>();
        services.AddScoped<LoanAdjustmentService>();
        services.AddSingleton<LendingMaintenanceService>();
        var bureauMode = configuration[$"{LendingSettings.SectionName}:CreditBureau:Mode"] ?? "Sandbox";
        if (string.Equals(bureauMode, "Live", StringComparison.OrdinalIgnoreCase)) services.AddSingleton<ICreditBureau, LiveCreditBureauNotConfigured>();
        else services.AddSingleton<ICreditBureau, SandboxCreditBureau>();
        services.AddSingleton<IModuleEndpoints, LendingEndpoints>();
        return services;
    }

    /// <summary>Daily accrual + bureau retention purge. The API host calls this; tools and tests do not.</summary>
    public static IServiceCollection AddLendingScheduler(this IServiceCollection services)
    {
        services.AddHostedService(sp => sp.GetRequiredService<LendingMaintenanceService>());
        return services;
    }
}
