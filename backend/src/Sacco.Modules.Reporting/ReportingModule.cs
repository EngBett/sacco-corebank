using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sacco.Modules.Reporting.Application;
using Sacco.Modules.Reporting.Endpoints;
using Sacco.Modules.Reporting.Persistence;
using Sacco.Shared.Http;
using Sacco.Shared.Persistence;

namespace Sacco.Modules.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddModuleDbContext<ReportingDbContext>(connectionString, ReportingDbContext.SchemaName);
        services.Configure<ReportingSettings>(configuration.GetSection(ReportingSettings.SectionName));
        services.AddScoped<StatutoryReportService>();
        services.AddScoped<DailyDigestService>();
        services.AddSingleton<HtmlToPdfRenderer>();
        services.AddSingleton<IModuleEndpoints, ReportingEndpoints>();
        return services;
    }

    /// <summary>Nightly PDF digest (ADR 0013, <c>Reporting:DailyDigest</c>). API host only — mirrors <c>AddLendingScheduler</c>.</summary>
    public static IServiceCollection AddReportingScheduler(this IServiceCollection services)
    {
        services.AddSingleton<DailyDigestScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<DailyDigestScheduler>());
        return services;
    }
}
