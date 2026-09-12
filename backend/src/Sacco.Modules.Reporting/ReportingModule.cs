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
        services.AddSingleton<IModuleEndpoints, ReportingEndpoints>();
        return services;
    }
}
