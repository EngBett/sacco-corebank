using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Members.Endpoints;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;
using Shouldly;

namespace Sacco.IntegrationTests.Identity;

/// <summary>Members sign in with phone + PIN and see only their own records; staff tokens cannot use the self endpoints and member tokens cannot use staff ones.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SelfServiceTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, realAuth: true);
    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> MemberClient(string phone = "254700100001", string pin = MembersSeeder.DemoPin)
    {
        var client = _factory.ClientWithToken(await _factory.TokenFor(phone, pin));
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        return client;
    }

    [Fact]
    public async Task Member_sees_own_profile_accounts_loans_and_statements_only()
    {
        var me = await MemberClient(); // M00001
        var profile = await (await me.GetAsync("/api/self/profile")).ReadAs<MyProfileResponse>();
        profile.MemberNumber.ShouldBe("M00001");
        profile.PhoneNumber.ShouldBe("254700100001");

        var accounts = await (await me.GetAsync("/api/self/accounts")).ReadAs<List<SavingsAccountResponse>>();
        accounts.ShouldNotBeEmpty();
        accounts.ShouldAllBe(a => a.MemberId == DemoTenant.MemberId("M00001"));

        var loans = await (await me.GetAsync("/api/self/loans")).ReadAs<List<MemberLoanSnapshot>>();
        loans.ShouldContain(l => l.LoanNumber == "LN-000001");

        (await me.GetAsync($"/api/self/statements/{accounts[0].AccountNumber}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.GetAsync("/api/self/statements/M00002-SV")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another member's account is invisible, not forbidden");

        // No staff surface at all.
        (await me.GetAsync("/api/members")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await me.GetAsync("/api/loans")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await me.GetAsync("/api/notifications")).StatusCode.ShouldBe(HttpStatusCode.OK, "notifications are per-login and harmless");
    }

    [Fact]
    public async Task Staff_tokens_are_refused_on_self_endpoints_and_wrong_pins_are_rejected()
    {
        var staff = _factory.ClientWithToken(await _factory.TokenFor("teller", IdentitySeeder.DemoPassword));
        staff.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        (await staff.GetAsync("/api/self/accounts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await Should.ThrowAsync<InvalidOperationException>(() => _factory.TokenFor("254700100001", "9999"));
    }

    [Fact]
    public async Task Member_can_apply_for_a_loan_and_start_a_sandbox_top_up()
    {
        var me = await MemberClient("254700100003"); // M00003: verified, funded deposits, no application in progress
        var loan = await (await me.PostAsJsonAsync("/api/self/loans", new Sacco.Modules.Lending.Endpoints.SelfApplyLoanRequest("EMG-LOAN", 5_000m, 6, "School uniforms", BureauConsent: true), HttpExtensions.JsonOptions)).ReadAs<MemberLoanSnapshot>();
        loan.Status.ShouldBe(LoanStatus.Applied);
        (await me.PostAsJsonAsync("/api/self/loans", new Sacco.Modules.Lending.Endpoints.SelfApplyLoanRequest("EMG-LOAN", 5_000m, 6, "again", BureauConsent: true), HttpExtensions.JsonOptions)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var accounts = await (await me.GetAsync("/api/self/accounts")).ReadAs<List<SavingsAccountResponse>>();
        var fosa = accounts.First(a => a.Kind == Sacco.Shared.Savings.ProductKind.FosaCurrent);
        var tx = await me.PostAsJsonAsync("/api/self/payments/topup", new Sacco.Modules.Payments.Endpoints.SelfTopUpRequest("MPesa", 500m, fosa.AccountNumber, null), HttpExtensions.JsonOptions);
        tx.StatusCode.ShouldBe(HttpStatusCode.Created);
        var other = await me.PostAsJsonAsync("/api/self/payments/topup", new Sacco.Modules.Payments.Endpoints.SelfTopUpRequest("MPesa", 500m, "M00002-FO", null), HttpExtensions.JsonOptions);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Row_level_security_is_forced_so_an_untenanted_connection_sees_nothing()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().HasTenant.ShouldBeFalse();
        var db = scope.ServiceProvider.GetRequiredService<Sacco.Modules.Members.Persistence.MembersDbContext>();
        (await db.Members.IgnoreQueryFilters().CountAsync()).ShouldBe(0, "FORCE ROW LEVEL SECURITY binds the owner too; without app.tenant_id no rows are visible");
        using var tenantScope = _factory.TenantScope();
        var db2 = tenantScope.ServiceProvider.GetRequiredService<Sacco.Modules.Members.Persistence.MembersDbContext>();
        (await db2.Members.IgnoreQueryFilters().CountAsync()).ShouldBeGreaterThan(0);
    }
}
