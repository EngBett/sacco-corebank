using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Identity.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Auth;
using Shouldly;

namespace Sacco.IntegrationTests.Identity;

/// <summary>Phase 2 exit criterion: log in as each seeded role, see role-appropriate access, and a permission-gated action denies an unauthorized role.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthenticationTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, realAuth: true);
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Every_seeded_role_can_log_in_and_sees_its_own_permissions()
    {
        foreach (var (id, userName, _, _, roleName) in IdentitySeeder.Users)
        {
            var token = await _factory.TokenFor(userName, IdentitySeeder.DemoPassword);
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            jwt.Subject.ShouldBe(id.ToString());
            jwt.Claims.Single(c => c.Type == "tenant").Value.ShouldBe(DemoTenant.Slug);
            jwt.Claims.ShouldNotContain(c => c.Type == "permission", "permissions are resolved server-side, never baked into the token");
            // The office a staff member works at travels in the token, so their postings and audit rows are stamped with it (ADR 0018).
            jwt.Claims.SingleOrDefault(c => c.Type == "branch_id").ShouldNotBeNull($"{userName} works at a branch").Value.ShouldNotBeNullOrWhiteSpace();

            var me = await (await _factory.ClientWithToken(token).GetAsync("/api/me")).ReadAs<MeResponse>();
            me.Roles.ShouldBe([roleName]);
            var expected = IdentitySeeder.Roles.Single(r => r.Name == roleName).Permissions;
            me.Permissions.ShouldBe(expected.OrderBy(p => p), ignoreOrder: false);
        }
    }

    [Fact]
    public async Task Permission_gated_endpoint_denies_unauthorized_role_and_allows_authorized_one()
    {
        var teller = _factory.ClientWithToken(await _factory.TokenFor("teller", IdentitySeeder.DemoPassword));
        var compliance = _factory.ClientWithToken(await _factory.TokenFor("compliance", IdentitySeeder.DemoPassword));

        (await teller.GetAsync("/api/admin/audit-log")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await compliance.GetAsync("/api/admin/audit-log")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await teller.GetAsync("/api/ledger/trial-balance")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await compliance.GetAsync("/api/ledger/trial-balance")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Wrong_password_unknown_tenant_and_missing_token_are_rejected()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => _factory.TokenFor("teller", "wrong-password-123"));
        await Should.ThrowAsync<InvalidOperationException>(() => _factory.TokenFor("teller", IdentitySeeder.DemoPassword, tenant: "nosuch"));
        var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        (await anonymous.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_tenant_must_match_the_requested_tenant()
    {
        var client = _factory.ClientWithToken(await _factory.TokenFor("compliance", IdentitySeeder.DemoPassword));
        client.DefaultRequestHeaders.Add("X-Tenant", "some-other-sacco");
        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // unknown tenant slug
    }

    [Fact]
    public async Task Role_permission_changes_take_effect_immediately_without_a_new_token()
    {
        var admin = _factory.ClientWithToken(await _factory.TokenFor("admin", IdentitySeeder.DemoPassword));
        var roles = await (await admin.GetAsync("/api/admin/roles")).ReadAs<List<RoleResponse>>();
        var tellerRole = roles.Single(r => r.Name == "Teller");

        var tellerToken = await _factory.TokenFor("teller", IdentitySeeder.DemoPassword);
        var teller = _factory.ClientWithToken(tellerToken);
        (await teller.GetAsync("/api/ledger/trial-balance")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        try
        {
            var granted = tellerRole.Permissions.Append(Permissions.Ledger.View).ToList();
            (await admin.PutAsJsonAsync($"/api/admin/roles/{tellerRole.Id}", new SaveRoleRequest(tellerRole.Name, tellerRole.Description, granted))).EnsureSuccessStatusCode();
            (await teller.GetAsync("/api/ledger/trial-balance")).StatusCode.ShouldBe(HttpStatusCode.OK, "same token, new permission — resolved server-side");
        }
        finally
        {
            (await admin.PutAsJsonAsync($"/api/admin/roles/{tellerRole.Id}", new SaveRoleRequest(tellerRole.Name, tellerRole.Description, tellerRole.Permissions))).EnsureSuccessStatusCode();
        }
        (await teller.GetAsync("/api/ledger/trial-balance")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "revocation is immediate");
    }

    [Fact]
    public async Task Discovery_document_and_login_page_are_served()
    {
        var client = _factory.CreateClient();
        var disco = await client.GetStringAsync("/.well-known/openid-configuration");
        disco.ShouldContain("\"issuer\":\"http://localhost\"");
        disco.ShouldContain("/connect/authorize");
        var login = await client.GetAsync("/account/login?tenant=demo");
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await login.Content.ReadAsStringAsync();
        html.ShouldContain("Icodeio SACCO");
        html.ShouldContain($"src=\"{DemoTenant.LogoUrl}\"");
        html.ShouldContain($"<link rel=\"icon\" href=\"{DemoTenant.FaviconUrl}\">");
        // The SACCO is never typed in: it comes from configuration/the request and is posted back as a hidden field.
        html.ShouldContain("<input type=\"hidden\" name=\"tenant\" value=\"demo\">");
        html.ShouldNotContain("<label for=\"tenant\">");
    }
}
