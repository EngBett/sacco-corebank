using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
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
public sealed class ApiFactory(string connectionString, bool realAuth = false, bool demoMode = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sacco", connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Tenancy:DefaultTenantSlug", "");
        builder.UseSetting("IdentityServer:IssuerUri", "http://localhost");
        builder.UseSetting("IdentityServer:SigningCredential", "Developer");
        builder.UseSetting("Identity:Accounts:PublicOrigin", "http://localhost");
        builder.UseSetting("Identity:Mfa:ActiveKeyId", "test");
        builder.UseSetting("Identity:Mfa:EncryptionKeys:test", MfaTestKey);
        builder.UseSetting("RateLimiting:AccountPermitsPerMinute", "1000");
        builder.UseSetting("IdentityServer:Clients:0:ClientId", "sacco-cli");
        builder.UseSetting("IdentityServer:Clients:0:GrantTypes:0", "password");
        builder.UseSetting("IdentityServer:Clients:0:ClientSecret", CliSecret);
        builder.UseSetting("Turnstile:Sandbox", "true");
        builder.UseSetting("Demo:Enabled", demoMode ? "true" : "false");
        builder.UseSetting("PublicApi:ApiKey", PublicApiKey);
        builder.UseSetting("Lending:Maintenance:Enabled", "false"); // tests call RunOnceAsync explicitly
        builder.UseSetting("Reporting:DailyDigest:Enabled", "false"); // tests call RunOnceAsync/RunForCurrentTenantAsync explicitly

        builder.ConfigureServices(services =>
        {
            // TestServer has no socket behind the request, so nothing sets a client address; a real host always does.
            services.AddSingleton<IStartupFilter, ClientAddressStartupFilter>();

            services.RemoveAll<Sacco.Shared.Notifications.ISmsSender>();
            services.AddSingleton<Sacco.Shared.Notifications.ISmsSender>(Sms);
            services.RemoveAll<Sacco.Shared.Notifications.IEmailSender>();
            services.AddSingleton<Sacco.Shared.Notifications.IEmailSender>(Email);

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
    /// <summary>Test-only AES key for authenticator secrets (32 zero-free bytes, base64).</summary>
    public const string MfaTestKey = "dGVzdC1tZmEta2V5LWZvci1pbnRlZ3JhdGlvbi10ZXM=";
    public const string PublicApiKey = "test-public-api-key";

    /// <summary>Captures every SMS this factory's app instance sends — read an OTP back the same way a member would.</summary>
    public TestSmsSender Sms { get; } = new();

    /// <summary>Captures every email this factory's app instance sends (activation and password reset links).</summary>
    public TestEmailSender Email { get; } = new();

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

    /// <summary>An HttpClient acting as the given user with the given permissions, on the Icodeio SACCO tenant.</summary>
    public HttpClient ClientAs(Guid userId, params string[] permissions) => ClientAtBranch(userId, null, permissions);

    /// <summary>An HttpClient acting as a user who works at the given branch — their postings and audit entries are stamped with it.</summary>
    public HttpClient ClientAtBranch(Guid userId, Guid? branchId, params string[] permissions)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (branchId is Guid branch) client.DefaultRequestHeaders.Add(TestAuthHandler.BranchHeader, branch.ToString());
        if (permissions.Length > 0) client.DefaultRequestHeaders.Add(TestAuthHandler.PermissionsHeader, string.Join(',', permissions));
        return client;
    }

    /// <summary>A service scope with the Icodeio SACCO tenant resolved — for calling module services directly.</summary>
    public IServiceScope TenantScope()
    {
        var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(DemoTenant.Id, DemoTenant.Slug);
        return scope;
    }
}

/// <summary>Gives every test request a client address, so audit rows record where a call came from as they do in production.</summary>
internal sealed class ClientAddressStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress ??= System.Net.IPAddress.Loopback;
            await nextMiddleware();
        });
        next(app);
    };
}

public static class HttpExtensions
{
    public static async Task<T> ReadAs<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
