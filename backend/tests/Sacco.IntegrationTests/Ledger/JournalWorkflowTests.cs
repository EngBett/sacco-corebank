using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Ledger.Domain;
using Sacco.Modules.Ledger.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.IntegrationTests.Ledger;

[Collection(DatabaseCollection.Name)]
public sealed class JournalWorkflowTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private static CreateJournalRequest ManualJournal(string reference, decimal amount) => new(reference, "Bank charges correction", null,
    [
        new(Coa.FosaBankCharges, Segment.Fosa, EntryDirection.Debit, amount, null, "Charge"),
        new(Coa.FosaCashAtBank, Segment.Fosa, EntryDirection.Credit, amount, null, "Charge"),
    ]);

    [Fact]
    public async Task Manual_journal_requires_a_different_approver_and_then_moves_balances()
    {
        var maker = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.JournalCreate, Permissions.Ledger.JournalApprove, Permissions.Ledger.View);
        var checker = _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Ledger.JournalApprove, Permissions.Ledger.View);
        var reference = $"MJ-TEST-{Guid.NewGuid():N}"[..30];

        var before = await (await maker.GetAsync($"/api/ledger/gl-accounts/{Coa.FosaBankCharges}")).ReadAs<GlAccountResponse>();

        var created = await (await maker.PostAsJsonAsync("/api/ledger/journals", ManualJournal(reference, 1_250m))).ReadAs<JournalResponse>();
        created.Status.ShouldBe(JournalEntryStatus.PendingApproval);

        // Balances untouched while pending
        (await (await maker.GetAsync($"/api/ledger/gl-accounts/{Coa.FosaBankCharges}")).ReadAs<GlAccountResponse>()).Balance.ShouldBe(before.Balance);

        // Same user (even with approve permission) is refused — segregation of duties is enforced in code.
        var selfApprove = await maker.PostAsync($"/api/ledger/journals/{created.Id}/approve", null);
        selfApprove.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await selfApprove.Content.ReadAsStringAsync()).ShouldContain("maker_checker.same_user");

        var approved = await (await checker.PostAsync($"/api/ledger/journals/{created.Id}/approve", null)).ReadAs<JournalResponse>();
        approved.Status.ShouldBe(JournalEntryStatus.Posted);
        approved.ApprovedByUserId.ShouldBe(DemoTenant.Users.BranchManager);

        var after = await (await maker.GetAsync($"/api/ledger/gl-accounts/{Coa.FosaBankCharges}")).ReadAs<GlAccountResponse>();
        (after.Balance - before.Balance).ShouldBe(1_250m);

        // The decision is in the audit trail
        var audit = _factory.ClientAs(DemoTenant.Users.ComplianceOfficer, Permissions.Admin.AuditView);
        var log = await audit.GetStringAsync($"/api/admin/audit-log?entityType=JournalEntry&entityId={created.Id}");
        log.ShouldContain("ledger.journal.initiated");
        log.ShouldContain("ledger.journal.approved");
    }

    [Fact]
    public async Task Rejected_journal_never_touches_balances()
    {
        var maker = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.JournalCreate, Permissions.Ledger.View);
        var checker = _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Ledger.JournalApprove);
        var reference = $"MJ-REJ-{Guid.NewGuid():N}"[..30];
        var before = await (await maker.GetAsync($"/api/ledger/gl-accounts/{Coa.FosaBankCharges}")).ReadAs<GlAccountResponse>();

        var created = await (await maker.PostAsJsonAsync("/api/ledger/journals", ManualJournal(reference, 999m))).ReadAs<JournalResponse>();
        var rejected = await (await checker.PostAsJsonAsync($"/api/ledger/journals/{created.Id}/reject", new RejectJournalRequest("Duplicate of bank statement line"))).ReadAs<JournalResponse>();
        rejected.Status.ShouldBe(JournalEntryStatus.Rejected);

        var after = await (await maker.GetAsync($"/api/ledger/gl-accounts/{Coa.FosaBankCharges}")).ReadAs<GlAccountResponse>();
        after.Balance.ShouldBe(before.Balance);
    }

    [Fact]
    public async Task Reversal_is_itself_maker_checker_and_restores_balances()
    {
        var maker = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.JournalCreate, Permissions.Ledger.JournalReverse, Permissions.Ledger.View);
        var checker = _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Ledger.JournalApprove, Permissions.Ledger.View);
        var reference = $"MJ-REV-{Guid.NewGuid():N}"[..30];
        var before = await (await maker.GetAsync($"/api/ledger/gl-accounts/{Coa.FosaBankCharges}")).ReadAs<GlAccountResponse>();

        var created = await (await maker.PostAsJsonAsync("/api/ledger/journals", ManualJournal(reference, 400m))).ReadAs<JournalResponse>();
        await (await checker.PostAsync($"/api/ledger/journals/{created.Id}/approve", null)).ReadAs<JournalResponse>();

        var reversal = await (await maker.PostAsJsonAsync($"/api/ledger/journals/{created.Id}/reverse", new ReverseJournalRequest("Posted in error"))).ReadAs<JournalResponse>();
        reversal.Status.ShouldBe(JournalEntryStatus.PendingApproval);
        reversal.ReversalOfEntryId.ShouldBe(created.Id);
        reversal.Lines.Single(l => l.GlAccountCode == Coa.FosaBankCharges).Direction.ShouldBe(EntryDirection.Credit);

        var mid = await (await maker.GetAsync($"/api/ledger/gl-accounts/{Coa.FosaBankCharges}")).ReadAs<GlAccountResponse>();
        (mid.Balance - before.Balance).ShouldBe(400m);

        await (await checker.PostAsync($"/api/ledger/journals/{reversal.Id}/approve", null)).ReadAs<JournalResponse>();
        var original = await (await checker.GetAsync($"/api/ledger/journals/{created.Id}")).ReadAs<JournalResponse>();
        original.Status.ShouldBe(JournalEntryStatus.Reversed);
        original.ReversedByEntryId.ShouldBe(reversal.Id);

        var after = await (await maker.GetAsync($"/api/ledger/gl-accounts/{Coa.FosaBankCharges}")).ReadAs<GlAccountResponse>();
        after.Balance.ShouldBe(before.Balance);
    }

    [Fact]
    public async Task Duplicate_reference_is_a_conflict()
    {
        var maker = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.JournalCreate);
        var reference = $"MJ-DUP-{Guid.NewGuid():N}"[..30];
        (await maker.PostAsJsonAsync("/api/ledger/journals", ManualJournal(reference, 10m))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await maker.PostAsJsonAsync("/api/ledger/journals", ManualJournal(reference, 10m))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Segment_mismatch_and_unbalanced_entries_are_rejected_with_problem_details()
    {
        var maker = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.JournalCreate);
        var wrongTag = new CreateJournalRequest($"MJ-SEG-{Guid.NewGuid():N}"[..30], "Wrong tag", null,
        [
            new(Coa.FosaBankCharges, Segment.Bosa, EntryDirection.Debit, 10m, null, null), // 5500 is FOSA
            new(Coa.FosaCashAtBank, Segment.Bosa, EntryDirection.Credit, 10m, null, null),
        ]);
        var r = await maker.PostAsJsonAsync("/api/ledger/journals", wrongTag);
        r.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync()).ShouldContain("ledger.segment_mismatch");

        var unbalanced = new CreateJournalRequest($"MJ-UNB-{Guid.NewGuid():N}"[..30], "Unbalanced", null,
        [
            new(Coa.FosaBankCharges, Segment.Fosa, EntryDirection.Debit, 10m, null, null),
            new(Coa.FosaCashAtBank, Segment.Fosa, EntryDirection.Credit, 9m, null, null),
        ]);
        r = await maker.PostAsJsonAsync("/api/ledger/journals", unbalanced);
        r.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync()).ShouldContain("ledger.journal.unbalanced");
    }

    [Fact]
    public async Task Control_account_lines_must_name_a_sub_account()
    {
        var maker = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.JournalCreate);
        var req = new CreateJournalRequest($"MJ-CTL-{Guid.NewGuid():N}"[..30], "Control without sub-account", null,
        [
            new(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Debit, 10m, null, null),
            new(Coa.FosaSavingsControl, Segment.Fosa, EntryDirection.Credit, 10m, null, null),
        ]);
        var r = await maker.PostAsJsonAsync("/api/ledger/journals", req);
        r.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadAsStringAsync()).ShouldContain("ledger.control_requires_subaccount");
    }
}
