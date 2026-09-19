using Sacco.Modules.Platform.Domain;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Platform;

public class BranchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    private static Branch HeadOffice() => Branch.Create(Guid.NewGuid(), Guid.NewGuid(), "hq", "Head Office", isHeadOffice: true, Now);

    [Fact]
    public void Normalises_the_code_and_starts_active()
    {
        var branch = HeadOffice();
        branch.Code.ShouldBe("HQ");
        branch.IsActive.ShouldBeTrue();
        branch.IsHeadOffice.ShouldBeTrue();
    }

    [Theory]
    [InlineData("A")]
    [InlineData("TOO-LONG-CODE")]
    [InlineData("NK R")]
    public void Refuses_codes_that_would_not_read_in_a_report(string code) =>
        Should.Throw<DomainRuleException>(() => Branch.Create(Guid.NewGuid(), Guid.NewGuid(), code, "Branch", false, Now)).Code.ShouldBe("platform.branch.code_invalid");

    [Fact]
    public void The_head_office_cannot_be_closed()
    {
        var branch = HeadOffice();
        Should.Throw<DomainRuleException>(() => branch.Update("HQ", "Head Office", true, null, null, null, null, null, 0, isActive: false))
            .Code.ShouldBe("platform.branch.head_office_active");
    }
}

public class AuditEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class Context : IAuditContext
    {
        public string? ActorName => "Grace Wanjiku";
        public string? IpAddress => "41.90.64.12";
        public string? UserAgent => "Mozilla/5.0";
        public string? CorrelationId => "0af7651916cd43dd8448eb211c80319c";
        public Guid? BranchId { get; init; } = Guid.NewGuid();
    }

    private static AuditLogEntry Entry(string action, string? previousHash) =>
        AuditLogEntry.Create(Tenant, Now, new AuditEvent(action, "JournalEntry", "MJ-1", Guid.NewGuid()), new Context(), previousHash);

    [Fact]
    public void Captures_who_from_where_and_under_which_request()
    {
        var entry = Entry("ledger.journal.approved", null);
        entry.ActorName.ShouldBe("Grace Wanjiku");
        entry.IpAddress.ShouldBe("41.90.64.12");
        entry.UserAgent.ShouldBe("Mozilla/5.0");
        entry.CorrelationId.ShouldBe("0af7651916cd43dd8448eb211c80319c");
        entry.BranchId.ShouldNotBeNull();
        entry.Outcome.ShouldBe(AuditOutcome.Success);
        entry.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void Each_entry_hashes_its_contents_and_the_one_before_it()
    {
        var first = Entry("ledger.journal.initiated", null);
        var second = Entry("ledger.journal.approved", first.Hash);
        second.PreviousHash.ShouldBe(first.Hash);
        second.Hash.ShouldNotBe(first.Hash);
        first.ComputeHash().ShouldBe(first.Hash);
        second.ComputeHash().ShouldBe(second.Hash);
    }

    [Fact]
    public void Editing_a_stored_entry_no_longer_matches_its_hash()
    {
        var entry = Entry("savings.withdrawal.approved", null);
        var stored = entry.Hash;
        // What a tamperer would do straight in the database (the trigger blocks it; this proves detection as well).
        typeof(AuditLogEntry).GetProperty(nameof(AuditLogEntry.Action))!.SetValue(entry, "savings.withdrawal.rejected");
        entry.ComputeHash().ShouldNotBe(stored);
    }
}

public class AuditDetailsTests
{
    [Fact]
    public void Records_only_what_changed()
    {
        var details = AuditDetails.New()
            .Changed("rate", 1200, 1400)
            .Changed("name", "Development Loan", "Development Loan")
            .ToJson();
        details.ShouldBe("""{"rate":{"from":1200,"to":1400}}""");
    }

    [Fact]
    public void Shows_which_permissions_were_added_and_removed()
    {
        var json = AuditDetails.New().Diff("permissions", ["loans.view", "loans.approve"], ["loans.view", "loans.disburse"]).ToJson();
        json.ShouldContain("\"added\":[\"loans.disburse\"]");
        json.ShouldContain("\"removed\":[\"loans.approve\"]");
    }

    [Fact]
    public void Escapes_values_instead_of_breaking_the_json()
    {
        var json = AuditDetails.New().With("reason", "He said \"no\"").ToString();
        System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("reason").GetString().ShouldBe("He said \"no\"");
    }

    [Fact]
    public void Nothing_recorded_means_no_details_column()
    {
        AuditDetails.New().ToJson().ShouldBeNull();
        AuditDetails.New().Changed("x", 1, 1).IsEmpty.ShouldBeTrue();
    }
}
