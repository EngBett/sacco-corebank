using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sacco.Modules.Members.Application;
using Sacco.Modules.Members.Endpoints;
using Sacco.Modules.Members.Persistence;
using Sacco.Shared.Http;
using Sacco.Shared.Members;
using Sacco.Shared.Persistence;

namespace Sacco.Modules.Members;

public static class MembersModule
{
    public static IServiceCollection AddMembersModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddModuleDbContext<MembersDbContext>(connectionString, MembersDbContext.SchemaName);
        services.Configure<TurnstileOptions>(configuration.GetSection(TurnstileOptions.SectionName));
        services.Configure<PublicApiOptions>(configuration.GetSection(PublicApiOptions.SectionName));
        services.AddHttpClient(nameof(TurnstileVerifier));
        services.AddScoped<ITurnstileVerifier, TurnstileVerifier>();
        services.AddScoped<MemberService>();
        services.AddScoped<IMemberDirectory, MemberDirectory>();
        services.AddSingleton<IModuleEndpoints, MemberEndpoints>();
        return services;
    }
}
