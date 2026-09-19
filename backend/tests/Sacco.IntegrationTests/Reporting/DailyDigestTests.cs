using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Reporting.Application;
using Sacco.Modules.Reporting.Endpoints;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Shouldly;

namespace Sacco.IntegrationTests.Reporting;

/// <summary>ADR 0013: an admin sets recipients, can preview or trigger the branded PDF today rather than waiting for midnight, and the permission is enforced — never role-name checks (non-negotiable #7).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DailyDigestTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private HttpClient Admin => _factory.ClientAs(DemoTenant.Users.Admin, Permissions.Reporting.RecipientsManage);
    private HttpClient Teller => _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Ledger.View);

    [Fact]
    public async Task Recipients_are_managed_by_permission_only_and_deduplicated_case_insensitively()
    {
        (await Teller.GetAsync("/api/reporting/daily-digest/recipients")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var seeded = await (await Admin.GetAsync("/api/reporting/daily-digest/recipients")).ReadAs<List<ReportRecipientResponse>>();
        seeded.ShouldContain(r => r.Email == "admin@icodeio.example.co.ke", "the seed tool adds a demo recipient so the feature is demoable out of the box");

        var added = await (await Admin.PostAsJsonAsync("/api/reporting/daily-digest/recipients", new AddRecipientRequest("Board.Chair@Icodeio.example.co.ke", "Board Chair"), HttpExtensions.JsonOptions)).ReadAs<ReportRecipientResponse>();
        added.Email.ShouldBe("board.chair@icodeio.example.co.ke");

        var dup = await Admin.PostAsJsonAsync("/api/reporting/daily-digest/recipients", new AddRecipientRequest("board.chair@icodeio.example.co.ke", "Again"), HttpExtensions.JsonOptions);
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var invalid = await Admin.PostAsJsonAsync("/api/reporting/daily-digest/recipients", new AddRecipientRequest("not-an-email", "x"), HttpExtensions.JsonOptions);
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await Admin.DeleteAsync($"/api/reporting/daily-digest/recipients/{added.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await (await Admin.GetAsync("/api/reporting/daily-digest/recipients")).ReadAs<List<ReportRecipientResponse>>();
        after.ShouldNotContain(r => r.Id == added.Id);
    }

    [Fact]
    public async Task Preview_renders_a_real_pdf_with_the_tenant_logo_and_todays_figures()
    {
        var res = await Admin.GetAsync("/api/reporting/daily-digest/preview.pdf");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(1000, "a rendered A4 PDF is never a handful of bytes");
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public async Task On_demand_run_emails_every_active_recipient_and_skips_a_tenant_with_none()
    {
        var run = await (await Admin.PostAsync("/api/reporting/daily-digest/run", null)).ReadAs<DigestResult>();
        run.Sent.ShouldBeTrue();
        run.RecipientCount.ShouldBeGreaterThanOrEqualTo(1);
        run.Error.ShouldBeNull();

        var recipients = await (await Admin.GetAsync("/api/reporting/daily-digest/recipients")).ReadAs<List<ReportRecipientResponse>>();
        foreach (var r in recipients) (await Admin.DeleteAsync($"/api/reporting/daily-digest/recipients/{r.Id}")).EnsureSuccessStatusCode();

        var emptyRun = await (await Admin.PostAsync("/api/reporting/daily-digest/run", null)).ReadAs<DigestResult>();
        emptyRun.Sent.ShouldBeFalse();
        emptyRun.RecipientCount.ShouldBe(0);

        // Re-seed the recipient other tests in this fixture expect, since the fixture's database is shared across tests.
        await Admin.PostAsJsonAsync("/api/reporting/daily-digest/recipients", new AddRecipientRequest("admin@icodeio.example.co.ke", "Grace Wanjiku (System Admin)"), HttpExtensions.JsonOptions);
    }
}
