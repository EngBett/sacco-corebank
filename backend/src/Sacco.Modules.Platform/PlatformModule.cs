using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sacco.Modules.Platform.Application;
using Sacco.Modules.Platform.Endpoints;
using Sacco.Modules.Platform.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Http;
using Sacco.Shared.Persistence;

namespace Sacco.Modules.Platform;

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddMemoryCache();
        services.AddModuleDbContext<PlatformDbContext>(connectionString, PlatformDbContext.SchemaName);
        services.Configure<TenancyOptions>(configuration.GetSection(TenancyOptions.SectionName));
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddSingleton<IModuleEndpoints, PlatformEndpoints>();
        return services;
    }

    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
