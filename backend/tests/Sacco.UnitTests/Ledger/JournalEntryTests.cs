using Sacco.Modules.Ledger.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Ledger;

public class JournalEntryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Maker = Guid.NewGuid();
    private static readonly Guid Checker = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    private static JournalLineDraft Line(Segment seg, EntryDirection dir, decimal amount, string code = "1000")
        => new(Guid.NewGuid(), code, null, null, seg, dir, amount, null);

    private static JournalEntry Create(IReadOnlyList<JournalLineDraft> lines, JournalEntryStatus status = JournalEntryStatus.PendingApproval)
        => JournalEntry.Create(Guid.NewGuid(), Tenant, "REF-1", "Test", new DateOnly(2026, 9, 11), "Manual", Maker, Now, lines, status);

    [Fact]
    public void Balanced_entry_is_created_with_lines_numbered()
    {
        var e = Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Fosa, EntryDirection.Credit, 100m)]);
        e.TotalAmount.ShouldBe(100m);
        e.Lines.Select(l => l.LineNumber).ShouldBe([1, 2]);
        e.Status.ShouldBe(JournalEntryStatus.PendingApproval);
    }

    [Fact]
    public void Unbalanced_entry_is_rejected()
    {
        var ex = Should.Throw<DomainRuleException>(() => Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Fosa, EntryDirection.Credit, 90m)]));
        ex.Code.ShouldBe("ledger.journal.unbalanced");
    }

    [Fact]
    public void Entry_must_balance_within_each_segment()
    {
        // Debits FOSA, credits BOSA — balanced overall but not per segment.
        var ex = Should.Throw<DomainRuleException>(() => Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Bosa, EntryDirection.Credit, 100m)]));
        ex.Code.ShouldBe("ledger.journal.segment_unbalanced");
    }

    [Fact]
    public void Cross_segment_flow_via_clearing_accounts_is_accepted()
    {
        var e = Create(
        [
            Line(Segment.Bosa, EntryDirection.Debit, 100m, "1800"), Line(Segment.Bosa, EntryDirection.Credit, 100m, "1000"),
            Line(Segment.Fosa, EntryDirection.Debit, 100m, "1020"), Line(Segment.Fosa, EntryDirection.Credit, 100m, "2800"),
        ]);
        e.TotalAmount.ShouldBe(200m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_amounts_are_rejected(decimal amount)
    {
        Should.Throw<DomainRuleException>(() => Create([Line(Segment.Fosa, EntryDirection.Debit, amount), Line(Segment.Fosa, EntryDirection.Credit, amount)]))
            .Code.ShouldBe("ledger.journal.non_positive_amount");
    }

    [Fact]
    public void More_than_two_decimals_is_rejected()
    {
        Should.Throw<DomainRuleException>(() => Create([Line(Segment.Fosa, EntryDirection.Debit, 10.005m), Line(Segment.Fosa, EntryDirection.Credit, 10.005m)]))
            .Code.ShouldBe("ledger.journal.precision");
    }

    [Fact]
    public void Single_line_is_rejected()
    {
        Should.Throw<DomainRuleException>(() => Create([Line(Segment.Fosa, EntryDirection.Debit, 10m)])).Code.ShouldBe("ledger.journal.too_few_lines");
    }

    [Fact]
    public void Approval_by_maker_is_a_maker_checker_violation()
    {
        var e = Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Fosa, EntryDirection.Credit, 100m)]);
        Should.Throw<MakerCheckerViolationException>(() => e.Approve(Maker, Now));
        e.Status.ShouldBe(JournalEntryStatus.PendingApproval);
    }

    [Fact]
    public void Approval_by_different_user_posts_the_entry()
    {
        var e = Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Fosa, EntryDirection.Credit, 100m)]);
        e.Approve(Checker, Now);
        e.Status.ShouldBe(JournalEntryStatus.Posted);
        e.ApprovedByUserId.ShouldBe(Checker);
        e.PostedAt.ShouldBe(Now);
    }

    [Fact]
    public void Rejection_requires_reason_and_distinct_user()
    {
        var e = Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Fosa, EntryDirection.Credit, 100m)]);
        Should.Throw<MakerCheckerViolationException>(() => e.Reject(Maker, "nope"));
        Should.Throw<DomainRuleException>(() => e.Reject(Checker, " "));
        e.Reject(Checker, "Wrong account");
        e.Status.ShouldBe(JournalEntryStatus.Rejected);
        e.RejectionReason.ShouldBe("Wrong account");
    }

    [Fact]
    public void Posted_entry_cannot_be_approved_again()
    {
        var e = Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Fosa, EntryDirection.Credit, 100m)], JournalEntryStatus.Posted);
        Should.Throw<DomainRuleException>(() => e.Approve(Checker, Now)).Code.ShouldBe("ledger.journal.not_pending");
    }

    [Fact]
    public void Only_posted_entries_can_be_marked_reversed()
    {
        var pending = Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Fosa, EntryDirection.Credit, 100m)]);
        Should.Throw<DomainRuleException>(() => pending.MarkReversedBy(Guid.NewGuid()));
        var posted = Create([Line(Segment.Fosa, EntryDirection.Debit, 100m), Line(Segment.Fosa, EntryDirection.Credit, 100m)], JournalEntryStatus.Posted);
        posted.MarkReversedBy(Guid.NewGuid());
        posted.Status.ShouldBe(JournalEntryStatus.Reversed);
    }
}
