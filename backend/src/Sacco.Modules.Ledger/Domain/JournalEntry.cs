using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Ledger.Domain;

public enum JournalEntryStatus
{
    /// <summary>Manual journal awaiting a checker. Balances are untouched.</summary>
    PendingApproval = 1,
    Posted = 2,
    Rejected = 3,
    /// <summary>Posted, then fully reversed by a later entry.</summary>
    Reversed = 4,
}

/// <summary>
/// A balanced double-entry transaction. Immutable once posted; corrections are reversals.
/// <see cref="Reference"/> is unique per tenant and is the idempotency key for every caller.
/// </summary>
public class JournalEntry : TenantEntity
{
    private readonly List<JournalLine> _lines = [];
    private JournalEntry() { }

    public string Reference { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateOnly ValueDate { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public JournalEntryStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public Guid InitiatedByUserId { get; private set; }
    public DateTimeOffset InitiatedAt { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? PostedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? ReversalOfEntryId { get; private set; }

    /// <summary>Office the posting belongs to (ADR 0018). Null on entries raised before branches existed, or by background jobs.</summary>
    public Guid? BranchId { get; private set; }
    public Guid? ReversedByEntryId { get; private set; }
    public IReadOnlyList<JournalLine> Lines => _lines;

    /// <summary>Creates a validated, balanced entry. Does not touch balances — the posting engine does that.</summary>
    public static JournalEntry Create(Guid id, Guid tenantId, string reference, string description, DateOnly valueDate, string source,
        Guid initiatedByUserId, DateTimeOffset now, IReadOnlyList<JournalLineDraft> drafts, JournalEntryStatus initialStatus, Guid? reversalOfEntryId = null, Guid? branchId = null)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new DomainRuleException("ledger.journal.reference_required", "A journal reference is required.");
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainRuleException("ledger.journal.description_required", "A journal description is required.");
        if (initiatedByUserId == Guid.Empty)
            throw new DomainRuleException("ledger.journal.initiator_required", "The initiating user is required.");
        if (drafts.Count < 2)
            throw new DomainRuleException("ledger.journal.too_few_lines", "A journal needs at least one debit and one credit line.");
        if (initialStatus is not (JournalEntryStatus.PendingApproval or JournalEntryStatus.Posted))
            throw new ArgumentOutOfRangeException(nameof(initialStatus));

        decimal debits = 0, credits = 0;
        foreach (var d in drafts)
        {
            if (d.Amount <= 0m)
                throw new DomainRuleException("ledger.journal.non_positive_amount", "Every journal line amount must be greater than zero.");
            if (decimal.Round(d.Amount, 2) != d.Amount)
                throw new DomainRuleException("ledger.journal.precision", "Amounts must have at most two decimal places.");
            if (d.Direction == EntryDirection.Debit) debits += d.Amount; else credits += d.Amount;
        }
        if (debits != credits)
            throw new DomainRuleException("ledger.journal.unbalanced", $"Journal does not balance: debits {debits:N2} vs credits {credits:N2}.");
        if (debits == 0m)
            throw new DomainRuleException("ledger.journal.zero", "Journal total cannot be zero.");

        // Each segment must balance on its own so that the FOSA and BOSA trial balances each
        // balance independently. Cross-segment flows go through the inter-segment clearing
        // accounts (due to / due from), giving every line a home in exactly one segment (ADR 0002).
        foreach (var seg in drafts.Select(d => d.Segment).Distinct())
        {
            var segDebits = drafts.Where(d => d.Segment == seg && d.Direction == EntryDirection.Debit).Sum(d => d.Amount);
            var segCredits = drafts.Where(d => d.Segment == seg && d.Direction == EntryDirection.Credit).Sum(d => d.Amount);
            if (segDebits != segCredits)
                throw new DomainRuleException("ledger.journal.segment_unbalanced",
                    $"Journal does not balance within {seg}: debits {segDebits:N2} vs credits {segCredits:N2}. Route cross-segment flows through the inter-segment clearing accounts.");
        }

        var entry = new JournalEntry
        {
            Id = id,
            TenantId = tenantId,
            Reference = reference.Trim(),
            Description = description.Trim(),
            ValueDate = valueDate,
            Source = source,
            BranchId = branchId,
            Status = initialStatus,
            TotalAmount = debits,
            InitiatedByUserId = initiatedByUserId,
            InitiatedAt = now,
            PostedAt = initialStatus == JournalEntryStatus.Posted ? now : null,
            ReversalOfEntryId = reversalOfEntryId,
        };

        var n = 1;
        foreach (var d in drafts)
            entry._lines.Add(JournalLine.Create(entry.Id, n++, d));

        return entry;
    }

    /// <summary>Checker approval. Enforces segregation of duties in code.</summary>
    public void Approve(Guid approvingUserId, DateTimeOffset now)
    {
        if (Status != JournalEntryStatus.PendingApproval)
            throw new DomainRuleException("ledger.journal.not_pending", $"Journal {Reference} is not pending approval.");
        MakerChecker.EnsureDistinct(InitiatedByUserId, approvingUserId, $"journal {Reference}");
        ApprovedByUserId = approvingUserId;
        PostedAt = now;
        Status = JournalEntryStatus.Posted;
    }

    public void Reject(Guid rejectingUserId, string reason)
    {
        if (Status != JournalEntryStatus.PendingApproval)
            throw new DomainRuleException("ledger.journal.not_pending", $"Journal {Reference} is not pending approval.");
        MakerChecker.EnsureDistinct(InitiatedByUserId, rejectingUserId, $"journal {Reference}");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainRuleException("ledger.journal.rejection_reason_required", "A rejection reason is required.");
        ApprovedByUserId = rejectingUserId;
        RejectionReason = reason.Trim();
        Status = JournalEntryStatus.Rejected;
    }

    public void MarkReversedBy(Guid reversalEntryId)
    {
        if (Status != JournalEntryStatus.Posted)
            throw new DomainRuleException("ledger.journal.not_posted", $"Journal {Reference} is not posted and cannot be reversed.");
        ReversedByEntryId = reversalEntryId;
        Status = JournalEntryStatus.Reversed;
    }
}

public sealed record JournalLineDraft(Guid GlAccountId, string GlAccountCode, Guid? LedgerAccountId, string? LedgerAccountNumber, Segment Segment, EntryDirection Direction, decimal Amount, string? Narrative);

public class JournalLine
{
    private JournalLine() { }

    public Guid Id { get; private set; }
    public Guid JournalEntryId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid GlAccountId { get; private set; }
    public string GlAccountCode { get; private set; } = string.Empty;
    public Guid? LedgerAccountId { get; private set; }
    public string? LedgerAccountNumber { get; private set; }
    /// <summary>Explicit FOSA/BOSA tag on every line (ADR 0002).</summary>
    public Segment Segment { get; private set; }
    public EntryDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public string? Narrative { get; private set; }

    internal static JournalLine Create(Guid entryId, int lineNumber, JournalLineDraft d) => new()
    {
        Id = Guid.CreateVersion7(),
        JournalEntryId = entryId,
        LineNumber = lineNumber,
        GlAccountId = d.GlAccountId,
        GlAccountCode = d.GlAccountCode,
        LedgerAccountId = d.LedgerAccountId,
        LedgerAccountNumber = d.LedgerAccountNumber,
        Segment = d.Segment,
        Direction = d.Direction,
        Amount = d.Amount,
        Narrative = d.Narrative,
    };
}
