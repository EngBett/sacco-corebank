using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Ledger.Endpoints;
using Sacco.Modules.Members.Domain;
using Sacco.Modules.Members.Endpoints;
using Sacco.Modules.Platform.Endpoints;
using Sacco.Modules.Platform.Persistence;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Shouldly;

namespace Sacco.IntegrationTests.Platform;

/// <summary>
/// Branches as a dimension on people and postings (ADR 0018), and the audit trail's context, filters, export and
/// tamper-evidence (ADR 0019).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BranchesAndAuditTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    private readonly PostgresFixture _fixture = pg;
    public void Dispose() => _factory.Dispose();

    private static async Task Sql(Npgsql.NpgsqlConnection connection, string sql)
    {
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private HttpClient Admin => _factory.ClientAs(DemoTenant.Users.Admin, Permissions.Admin.TenantManage, Permissions.Admin.AuditView);

    [Fact]
    public async Task A_sacco_runs_several_offices_and_stamps_what_happens_at_each()
    {
        // The seed opens a head office and two branches; members can see where to find them.
        var branches = await (await Admin.GetAsync("/api/admin/branches")).ReadAs<List<BranchResponse>>();
        branches.Count.ShouldBeGreaterThanOrEqualTo(3);
        branches.Count(b => b.IsHeadOffice).ShouldBe(1, "exactly one office is the head office");
        var nakuru = branches.Single(b => b.Code == "NKR");

        var site = _factory.CreateClient();
        site.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        (await (await site.GetAsync("/api/public/branches")).ReadAs<List<PublicBranch>>()).ShouldContain(b => b.Code == "NKR" && b.Town == "Nakuru");

        // A new office: added, then corrected.
        var code = $"B{Guid.NewGuid():N}"[..5].ToUpperInvariant();
        var created = await (await Admin.PostAsJsonAsync("/api/admin/branches",
            new SaveBranchRequest(code, "Kericho Agency", false, "Kericho", "Kericho", "Moi Highway", "254700100050", null, 40), HttpExtensions.JsonOptions)).ReadAs<BranchResponse>();
        created.Code.ShouldBe(code);
        var renamed = await (await Admin.PutAsJsonAsync($"/api/admin/branches/{created.Id}",
            new SaveBranchRequest(code, "Kericho Branch", false, "Kericho", "Kericho", "Moi Highway", "254700100050", null, 40), HttpExtensions.JsonOptions)).ReadAs<BranchResponse>();
        renamed.Name.ShouldBe("Kericho Branch");
        (await Admin.PutAsJsonAsync($"/api/admin/branches/{created.Id}",
            new SaveBranchRequest(code, "Kericho Branch", false, "Kericho", "Kericho", "Moi Highway", null, null, 40, IsActive: false), HttpExtensions.JsonOptions))
            .StatusCode.ShouldBe(HttpStatusCode.OK, "a branch that is not the head office can be closed");

        // A member registered by Nakuru staff belongs to Nakuru, and can be listed by branch.
        var registrar = _factory.ClientAtBranch(DemoTenant.Users.LoanOfficer, nakuru.Id, Permissions.Members.Create, Permissions.Members.View, Permissions.Members.Edit);
        var details = new PersonalDetailsDto($"Branch{Guid.NewGuid():N}"[..12], null, "Member", Gender.Female, new DateOnly(1990, 5, 1),
            $"{Random.Shared.Next(10_000_000, 99_999_999)}", null, $"2547{Random.Shared.Next(10_000_000, 99_999_999)}", null, null, "Nakuru", null, null, null);
        var member = await (await registrar.PostAsJsonAsync("/api/members/", new SaveMemberRequest(details, new NextOfKinDto("Kin Name", "Sister", "254700111222")), HttpExtensions.JsonOptions))
            .ReadAs<MemberResponse>();
        member.BranchId.ShouldBe(nakuru.Id);
        var atNakuru = await (await registrar.GetAsync($"/api/members/?branchId={nakuru.Id}&pageSize=200")).ReadAs<PagedResult<MemberResponse>>();
        atNakuru.Items.ShouldContain(m => m.Id == member.Id);

        // Moving a member to another office is recorded.
        var eldoret = branches.Single(b => b.Code == "ELD");
        (await (await registrar.PutAsJsonAsync($"/api/members/{member.Id}/branch", new SetMemberBranchRequest(eldoret.Id), HttpExtensions.JsonOptions)).ReadAs<MemberResponse>())
            .BranchId.ShouldBe(eldoret.Id);
        var moves = await (await Admin.GetAsync($"/api/admin/audit-log?action=members.branch_changed&entityId={member.Id}")).ReadAs<PagedResult<AuditLogResponse>>();
        moves.Items.ShouldNotBeEmpty();

        // Money taken in at a branch carries that branch on the journal.
        var teller = _factory.ClientAtBranch(DemoTenant.Users.Teller, nakuru.Id, Permissions.Savings.Deposit, Permissions.Savings.View, Permissions.Ledger.View);
        var reference = $"TEST-DEP-{Guid.NewGuid():N}"[..20];
        await (await teller.PostAsJsonAsync($"/api/savings/accounts/M00003-FO/deposits",
            new DepositRequest(1_500m, Sacco.Shared.Savings.DepositChannel.Cash, reference, "Branch test deposit"), HttpExtensions.JsonOptions)).ReadAs<Sacco.Shared.Savings.DepositResult>();
        var journals = await (await teller.GetAsync($"/api/ledger/journals?branchId={nakuru.Id}&pageSize=200")).ReadAs<PagedResult<JournalResponse>>();
        journals.Items.ShouldContain(j => j.Reference.Contains(reference), "the deposit journal is stamped with the teller's branch");

        // …and so does the audit entry, with who, from where and under which request.
        var deposits = await (await Admin.GetAsync($"/api/admin/audit-log?action=savings.deposit&search={reference}")).ReadAs<PagedResult<AuditLogResponse>>();
        var deposit = deposits.Items.ShouldHaveSingleItem();
        deposit.ActorUserId.ShouldBe(DemoTenant.Users.Teller);
        deposit.BranchId.ShouldBe(nakuru.Id);
        deposit.ActorName.ShouldNotBeNullOrWhiteSpace();
        deposit.IpAddress.ShouldNotBeNullOrWhiteSpace();
        deposit.CorrelationId.ShouldNotBeNullOrWhiteSpace();
        deposit.Outcome.ShouldBe(AuditOutcome.Success);
        deposit.Details.ShouldContain("1500");
    }

    [Fact]
    public async Task The_audit_trail_can_be_searched_exported_and_shown_to_be_untouched()
    {
        // Something to find: a branding change, which records what changed.
        var branding = await (await _factory.CreateClient().WithTenant().GetAsync("/api/public/tenant/branding")).ReadAs<TenantBrandingResponse>();
        await Admin.PutAsJsonAsync("/api/admin/tenant/branding", new UpdateBrandingRequest(branding.PrimaryColor, branding.SecondaryColor, branding.AccentColor,
            branding.LogoUrl, "Growing together, one shilling at a time — audited", branding.SupportEmail, branding.SupportPhone, branding.FaviconUrl,
            branding.LightModeLogoUrl, branding.DarkModeLogoUrl), HttpExtensions.JsonOptions);

        var changes = await (await Admin.GetAsync("/api/admin/audit-log?action=platform.branding.updated")).ReadAs<PagedResult<AuditLogResponse>>();
        var change = changes.Items.First();
        change.Details.ShouldContain("tagline");
        change.Details.ShouldContain("audited");

        // Filters: by action prefix, by actor and by date.
        var byPrefix = await (await Admin.GetAsync("/api/admin/audit-log?action=platform.&pageSize=200")).ReadAs<PagedResult<AuditLogResponse>>();
        byPrefix.Items.ShouldAllBe(a => a.Action.StartsWith("platform."));
        var byActor = await (await Admin.GetAsync($"/api/admin/audit-log?actorUserId={DemoTenant.Users.Admin}&pageSize=200")).ReadAs<PagedResult<AuditLogResponse>>();
        byActor.Items.ShouldAllBe(a => a.ActorUserId == DemoTenant.Users.Admin);
        var tomorrow = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var future = await (await Admin.GetAsync($"/api/admin/audit-log?from={tomorrow}")).ReadAs<PagedResult<AuditLogResponse>>();
        future.TotalCount.ShouldBe(0);

        // Export: a CSV of the same rows, and the export is itself audited.
        var export = await Admin.GetAsync("/api/admin/audit-log/export.csv?action=platform.");
        export.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var csv = await export.Content.ReadAsStringAsync();
        csv.ShouldStartWith("occurred_at,action,outcome");
        csv.ShouldContain("platform.branding.updated");
        (await (await Admin.GetAsync("/api/admin/audit-log?action=platform.audit_log.exported")).ReadAs<PagedResult<AuditLogResponse>>()).Items.ShouldNotBeEmpty();

        // The chain proves the trail hasn't been edited.
        (await (await Admin.GetAsync("/api/admin/audit-log/verify")).ReadAs<AuditChainCheckResponse>()).Intact.ShouldBeTrue();

        // The database itself refuses edits and deletes.
        using var scope = _factory.TenantScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var id = change.Id;
        var update = await Should.ThrowAsync<Exception>(async () =>
            await db.Database.ExecuteSqlAsync($"UPDATE platform.audit_log SET details = '{{}}' WHERE id = {id}"));
        update.ToString().ShouldContain("append-only");
        var delete = await Should.ThrowAsync<Exception>(async () => await db.Database.ExecuteSqlAsync($"DELETE FROM platform.audit_log WHERE id = {id}"));
        delete.ToString().ShouldContain("append-only");

        // The application's own database role can't lift that guard either — only the table's owner can.
        (await Should.ThrowAsync<Exception>(async () =>
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE platform.audit_log DISABLE TRIGGER audit_log_append_only")))
            .ToString().ShouldContain("must be owner");

        // With the guard lifted by a database administrator, an edit is still detected.
        await using var dba = new Npgsql.NpgsqlConnection(_fixture.AdminConnectionString);
        await dba.OpenAsync();
        await Sql(dba, "ALTER TABLE platform.audit_log DISABLE TRIGGER audit_log_append_only");
        try
        {
            await Sql(dba, $"UPDATE platform.audit_log SET action = 'platform.branding.viewed' WHERE id = '{id}'");
            var broken = await (await Admin.GetAsync("/api/admin/audit-log/verify")).ReadAs<AuditChainCheckResponse>();
            broken.Intact.ShouldBeFalse();
            broken.FirstBrokenEntryId.ShouldBe(id);
            broken.Problem.ShouldNotBeNullOrWhiteSpace();

            await Sql(dba, $"UPDATE platform.audit_log SET action = 'platform.branding.updated' WHERE id = '{id}'");
            (await (await Admin.GetAsync("/api/admin/audit-log/verify")).ReadAs<AuditChainCheckResponse>()).Intact.ShouldBeTrue("putting the value back restores the chain");
        }
        finally
        {
            await Sql(dba, "ALTER TABLE platform.audit_log ENABLE TRIGGER audit_log_append_only");
        }

        // Reading the trail needs the audit permission.
        (await _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Savings.View).GetAsync("/api/admin/audit-log")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Demo_mode_is_off_unless_configured_and_then_both_websites_see_the_notice()
    {
        (await (await _factory.CreateClient().WithTenant().GetAsync("/api/public/tenant/branding")).ReadAs<TenantBrandingResponse>()).Demo.ShouldBeNull();

        using var demo = new ApiFactory(pg.ConnectionString, demoMode: true);
        var notice = (await (await demo.CreateClient().WithTenant().GetAsync("/api/public/tenant/branding")).ReadAs<TenantBrandingResponse>()).Demo;
        notice.ShouldNotBeNull();
        notice.Message.ShouldNotBeNullOrWhiteSpace();
    }
}

/// <summary>Mirrors <c>AuditChainCheck</c> for deserialisation (the API returns it as JSON).</summary>
public sealed record AuditChainCheckResponse(int Checked, bool Intact, Guid? FirstBrokenEntryId, DateTimeOffset? FirstBrokenAt, string? Problem, int Unchained);

public static class TenantClientExtensions
{
    public static HttpClient WithTenant(this HttpClient client, string tenant = DemoTenant.Slug)
    {
        client.DefaultRequestHeaders.Add("X-Tenant", tenant);
        return client;
    }
}
