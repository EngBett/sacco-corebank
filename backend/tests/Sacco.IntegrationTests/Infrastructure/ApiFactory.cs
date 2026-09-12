using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Sacco.Shared.Tenancy;

namespace Sacco.IntegrationTests.Infrastructure;

/// <param name="connectionString">Seeded database.</param>
/// <param name="realAuth">When true, the in-process Open.IdentityServer issues and validates real JWTs; otherwise the header-based test scheme is used.</param>
public sealed class ApiFactory(string connectionString, bool realAuth = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sacco", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Tenancy:DefaultTenantSlug", "");
        builder.UseSetting("IdentityServer:IssuerUri", "http://localhost");
        builder.UseSetting("IdentityServer:SigningCredential", "Developer");
        builder.UseSetting("IdentityServer:Clients:0:ClientId", "sacco-cli");
        builder.UseSetting("IdentityServer:Clients:0:GrantTypes:0", "password");
        builder.UseSetting("IdentityServer:Clients:0:ClientSecret", CliSecret);
        builder.UseSetting("Turnstile:Sandbox", "true");
        builder.UseSetting("PublicApi:ApiKey", PublicApiKey);

        builder.ConfigureServices(services =>
        {
            if (realAuth) return;
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.Configure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
            services.RemoveAll<IPermissionResolver>();
            services.AddScoped<IPermissionResolver, ClaimsPermissionResolver>();
        });
    }

    public const string CliSecret = "test-cli-secret";
    public const string PublicApiKey = "test-public-api-key";

    /// <summary>Obtains a real access token from /connect/token via the password grant (first-party demo client).</summary>
    public async Task<string> TokenFor(string userName, string password, string tenant = DemoTenant.Slug)
    {
        var client = CreateClient();
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = "sacco-cli", ["client_secret"] = CliSecret,
            ["username"] = userName, ["password"] = password, ["scope"] = "openid profile tenant sacco-api offline_access", ["tenant"] = tenant,
        }));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Token request failed ({(int)response.StatusCode}): {body}");
        return System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>An HttpClient acting as the given user with the given permissions, on the Demo SACCO tenant.</summary>
    public HttpClient ClientAs(Guid userId, params string[] permissions)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (permissions.Length > 0) client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>A service scope with the Demo SACCO tenant resolved — for calling module services directly.</summary>
    public IServiceScope TenantScope()
    {
        var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(DemoTenant.Id, DemoTenant.Slug);
        return scope;
    }
}

public static class HttpExtensions
{
    public static async Task<T> ReadAs<T>(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
