using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Lending.Application;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Endpoints;
using Sacco.Modules.Lending.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Shouldly;

namespace Sacco.IntegrationTests.Lending;

/// <summary>Write-off charges the provision and closes the loan; restructuring rebuilds the schedule; both need a second person and both leave a GL trail.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoanAdjustmentsTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private async Task<Loan> ActiveLoanOf(string memberNumber)
    {
        using var scope = _factory.TenantScope();
        return await scope.ServiceProvider.GetRequiredService<LendingDbContext>().Loans.AsNoTracking().FirstAsync(l => l.MemberId == DemoTenant.MemberId(memberNumber) && l.Status == LoanStatus.Active);
    }

    [Fact]
    public async Task Write_off_needs_a_checker_and_charges_the_provision()
    {
        var loan = await ActiveLoanOf("M00010"); // seeded Loss-bucket loan, 27 months overdue
        var officer = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Loans.View, Permissions.Loans.Restructure, Permissions.Loans.Approve);
        var committee = _factory.ClientAs(DemoTenant.Users.CreditCommittee1, Permissions.Loans.View, Permissions.Loans.Approve);

        var req = await (await officer.PostAsJsonAsync($"/api/loans/{loan.Id}/write-off", new ReasonRequest("Borrower untraceable; board resolution 07/2026"), HttpExtensions.JsonOptions)).ReadAs<LoanAdjustmentResponse>();
        req.Kind.ShouldBe(LoanAdjustmentKind.WriteOff);
        req.Status.ShouldBe(LoanAdjustmentStatus.PendingApproval);
        (await officer.PostAsJsonAsync($"/api/loans/{loan.Id}/write-off", new ReasonRequest("again"), HttpExtensions.JsonOptions)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Maker cannot be checker.
        (await officer.PostAsJsonAsync($"/api/loans/adjustments/{req.Id}/approve", new NotesRequest("self"), HttpExtensions.JsonOptions)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        decimal outstandingBefore;
        using (var scope = _factory.TenantScope())
            outstandingBefore = (await scope.ServiceProvider.GetRequiredService<ILedgerService>().FindAccountAsync(loan.LedgerAccountNumber!, CancellationToken.None))!.Balance;
        outstandingBefore.ShouldBeGreaterThan(0);

        var approved = await (await committee.PostAsJsonAsync($"/api/loans/adjustments/{req.Id}/approve", new NotesRequest("Approved"), HttpExtensions.JsonOptions)).ReadAs<LoanAdjustmentResponse>();
        approved.Status.ShouldBe(LoanAdjustmentStatus.Approved);
        approved.PrincipalWrittenOff.ShouldBe(outstandingBefore);
        approved.JournalEntryId.ShouldNotBeNull();

        var after = await (await officer.GetAsync($"/api/loans/{loan.Id}")).ReadAs<LoanResponse>();
        after.Status.ShouldBe(LoanStatus.WrittenOff);
        after.OutstandingPrincipal.ShouldBe(0m);
        after.Adjustments.Single().Kind.ShouldBe(LoanAdjustmentKind.WriteOff);
        using (var scope = _factory.TenantScope())
            (await scope.ServiceProvider.GetRequiredService<ILedgerService>().FindAccountAsync(loan.LedgerAccountNumber!, CancellationToken.None))!.Status.ShouldBe(LedgerAccountStatus.Closed);
    }

    [Fact]
    public async Task Restructure_rebuilds_the_schedule_on_approval()
    {
        var loan = await ActiveLoanOf("M00006"); // seeded Doubtful development loan: 120,000 over 36 months (product max 48)
        var officer = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Loans.View, Permissions.Loans.Restructure);
        var committee = _factory.ClientAs(DemoTenant.Users.CreditCommittee2, Permissions.Loans.View, Permissions.Loans.Approve);

        var bad = await officer.PostAsJsonAsync($"/api/loans/{loan.Id}/restructure", new RestructureRequest(99, null, "too long"), HttpExtensions.JsonOptions);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var req = await (await officer.PostAsJsonAsync($"/api/loans/{loan.Id}/restructure", new RestructureRequest(42, 1000, "Member on half salary after hospitalisation"), HttpExtensions.JsonOptions)).ReadAs<LoanAdjustmentResponse>();
        await committee.PostAsJsonAsync($"/api/loans/adjustments/{req.Id}/approve", new NotesRequest("ok"), HttpExtensions.JsonOptions);

        var after = await (await officer.GetAsync($"/api/loans/{loan.Id}")).ReadAs<LoanResponse>();
        after.Status.ShouldBe(LoanStatus.Active);
        after.TermMonths.ShouldBe(42);
        after.InterestRateBps.ShouldBe(1000);
        after.RestructureCount.ShouldBe(1);
        after.Schedule.Count.ShouldBe(42);
        after.Schedule.Sum(i => i.PrincipalDue).ShouldBe(after.OutstandingPrincipal);
        after.DaysInArrears.ShouldBe(0, "a fresh schedule starts current");
    }

    [Fact]
    public async Task Applications_record_bureau_consent_and_refuse_without_it()
    {
        var officer = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Loans.View, Permissions.Loans.Originate);
        var consent = await (await officer.GetAsync("/api/loans/bureau-consent")).ReadAs<BureauConsentInfo>();
        consent.Required.ShouldBeTrue();
        consent.Text.ShouldContain("credit reference bureau");
        var noConsent = await officer.PostAsJsonAsync("/api/loans", new ApplyLoanRequest(DemoTenant.MemberId("M00003"), "EMG-LOAN", 5_000m, 6, "Test", null, BureauConsent: false), HttpExtensions.JsonOptions);
        noConsent.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await noConsent.Content.ReadAsStringAsync()).ShouldContain("loans.bureau_consent_required");
    }

    [Fact]
    public async Task Maintenance_runs_for_every_tenant_and_reports()
    {
        var maintenance = _factory.Services.GetRequiredService<LendingMaintenanceService>();
        var result = await maintenance.RunOnceAsync(CancellationToken.None);
        result.Tenants.ShouldBeGreaterThanOrEqualTo(1);
        result.Errors.ShouldBeEmpty();
    }
}
