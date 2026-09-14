using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Open.IdentityServer;
using Open.IdentityServer.Stores;
using Sacco.Modules.Identity.Application;
using Sacco.Modules.Identity.Endpoints;
using Sacco.Modules.Identity.IdentityServer;
using Sacco.Modules.Identity.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Persistence;

namespace Sacco.Modules.Identity;

public static class IdentityModule
{
    /// <summary>
    /// Registers users/roles/permissions and hosts Open.IdentityServer in-process (ADR 0005).
    /// The caller must have registered <see cref="ITenantLookup"/> (implemented over the Platform module).
    /// </summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment env, string connectionString)
    {
        services.AddMemoryCache();
        services.AddModuleDbContext<IdentityDbContext>(connectionString, IdentityDbContext.SchemaName);
        services.AddScoped<PermissionResolver>();
        services.Replace(ServiceDescriptor.Scoped<IPermissionResolver>(sp => sp.GetRequiredService<PermissionResolver>()));
        services.AddScoped<UserService>();
        services.AddScoped<RoleService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IHubTicketIssuer, HubTicketIssuer>();
        services.AddSingleton<IModuleEndpoints, IdentityAdminEndpoints>();
        services.AddSingleton<IModuleEndpoints, AccountEndpoints>();

        var settings = configuration.GetSection(IdentityServerSettings.SectionName).Get<IdentityServerSettings>() ?? new IdentityServerSettings();
        services.AddSingleton(settings);

        var builder = services.AddIdentityServer(o =>
            {
                o.IssuerUri = settings.IssuerUri;
                o.EmitStaticAudienceClaim = true;
                o.Authentication.CookieAuthenticationScheme = IdentityServerConstants.DefaultCookieAuthenticationScheme;
                // Lax (not the IdentityServer4 default of None): our flows are top-level redirects only, and browsers reject
                // SameSite=None cookies without Secure, which broke sign-in over plain http in development.
                o.Authentication.CookieSameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                o.Authentication.CheckSessionCookieSameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                o.UserInteraction.LoginUrl = "/account/login";
                o.UserInteraction.LogoutUrl = "/account/logout";
                o.UserInteraction.ErrorUrl = "/account/error";
            })
            .AddInMemoryIdentityResources(IdentityServerConfig.IdentityResources)
            .AddInMemoryApiScopes(IdentityServerConfig.ApiScopes)
            .AddInMemoryApiResources(IdentityServerConfig.ApiResources)
            .AddInMemoryClients(IdentityServerConfig.Clients(settings))
            .AddProfileService<SaccoProfileService>()
            .AddResourceOwnerValidator<SaccoPasswordValidator>()
            .AddPersistedGrantStore<PostgresPersistedGrantStore>();

        var useCertificate = string.Equals(settings.SigningCredential, "Certificate", StringComparison.OrdinalIgnoreCase);
        if (useCertificate)
            builder.AddSigningCredential(LoadCertificate(settings));
        else
            builder.AddDeveloperSigningCredential(persistKey: true, filename: Path.Combine(AppContext.BaseDirectory, "sacco-dev-signing-key.jwk"));

        // Production must never run on the developer key. Checked when the host *starts* (not when it is
        // merely built, e.g. by the build-time OpenAPI generator) — see docs/runbooks/production-cutover.md.
        services.AddOptions<IdentityServerSettings>()
            .Configure(o => o.SigningCredential = settings.SigningCredential)
            .Validate(_ => !env.IsProduction() || useCertificate, "IdentityServer:SigningCredential must be 'Certificate' in Production.")
            .ValidateOnStart();

        // AddIdentityServer makes its cookie the default scheme; the API's default must stay bearer.
        // The interactive /account endpoints sign in/out with the "idsrv" cookie scheme explicitly.
        services.Configure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
        {
            o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultSignInScheme = IdentityServerConstants.DefaultCookieAuthenticationScheme;
            o.DefaultSignOutScheme = IdentityServerConstants.DefaultCookieAuthenticationScheme;
        });

        // The API validates its own issuer's tokens locally (no HTTP discovery round-trip to itself).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IServiceProvider>((o, sp) =>
            {
                o.Authority = null;
                o.TokenValidationParameters.ValidIssuer = settings.IssuerUri;
                o.TokenValidationParameters.ValidateIssuer = true;
                o.TokenValidationParameters.ValidAudience = IdentityServerConfig.ApiScopeName;
                o.TokenValidationParameters.ValidateAudience = true;
                o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) =>
                {
                    using var scope = sp.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IValidationKeysStore>();
                    return store.GetValidationKeysAsync().GetAwaiter().GetResult().Select(k => k.Key).ToList();
                };
            });

        // Hub tickets (SignalR): same issuer and keys, different audience, token read from the query string
        // on hub paths only. Registered as its own scheme so it is never consulted for REST requests.
        services.AddAuthentication().AddJwtBearer(HubTicketAuth.Scheme, _ => { });
        services.AddOptions<JwtBearerOptions>(HubTicketAuth.Scheme)
            .Configure<IServiceProvider>((o, sp) =>
            {
                o.Authority = null;
                o.MapInboundClaims = false;
                o.TokenValidationParameters.NameClaimType = "name";
                o.TokenValidationParameters.ValidIssuer = settings.IssuerUri;
                o.TokenValidationParameters.ValidateIssuer = true;
                o.TokenValidationParameters.ValidAudience = HubTicketAuth.Audience;
                o.TokenValidationParameters.ValidateAudience = true;
                o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, _, _) =>
                {
                    using var scope = sp.CreateScope();
                    var store = scope.ServiceProvider.GetRequiredService<IValidationKeysStore>();
                    return store.GetValidationKeysAsync().GetAwaiter().GetResult().Select(k => k.Key).ToList();
                };
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(HubTicketAuth.PathPrefix))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    private static X509Certificate2 LoadCertificate(IdentityServerSettings s)
    {
        if (!string.IsNullOrEmpty(s.CertificateBase64))
            return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(s.CertificateBase64), s.CertificatePassword);
        if (!string.IsNullOrEmpty(s.CertificatePath))
            return X509CertificateLoader.LoadPkcs12FromFile(s.CertificatePath, s.CertificatePassword);
        throw new InvalidOperationException("IdentityServer:CertificatePath or CertificateBase64 is required when SigningCredential is 'Certificate'.");
    }

    public static IApplicationBuilder UseSaccoIdentityServer(this IApplicationBuilder app) => app.UseIdentityServer();
}
