using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddScoped<Sacco.Shared.Tenancy.ITenantEnumerator, TenantDirectory>();
        services.AddSingleton(configuration.GetSection(Sacco.Shared.Http.DemoSettings.SectionName).Get<Sacco.Shared.Http.DemoSettings>() ?? new Sacco.Shared.Http.DemoSettings());
        services.AddScoped<BranchService>();
        services.AddScoped<Sacco.Shared.Tenancy.IBranchDirectory>(sp => sp.GetRequiredService<BranchService>());
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<AuditVerifier>();
        // The API host registers the request-aware versions of both before it adds this module, so these only take effect
        // in hosts without an HTTP request (the seed tool, background workers).
        services.TryAddScoped<IAuditContext, NullAuditContext>();
        services.TryAddScoped<Sacco.Shared.Auth.ICurrentUser, Sacco.Shared.Auth.NullCurrentUser>();
        services.AddSingleton<IModuleEndpoints, PlatformEndpoints>();
        return services;
    }

    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}
