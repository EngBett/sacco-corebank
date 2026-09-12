using Sacco.Shared.Domain;
using Sacco.Shared.Payments;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Payments.Domain;

public class PaymentPurpose
{
    public PaymentPurposeType Type { get; set; }
    public string? AccountNumber { get; set; }
    public string? LoanNumber { get; set; }
    public Guid? WithdrawalId { get; set; }
}

/// <summary>
/// One collection (member → SACCO) or disbursement (SACCO → member) through a provider. The
/// saga drives it; this row is the auditable business record and the reconciliation queue.
/// </summary>
public class PaymentTransaction : TenantEntity
{
    private PaymentTransaction() { }

    public PaymentKind Kind { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    /// <summary>Phone number (mobile money) or bank account (bank transfer).</summary>
    public string Counterparty { get; private set; } = string.Empty;
    public Guid? MemberId { get; private set; }
    public PaymentPurpose Purpose { get; private set; } = new();
    /// <summary>Our reference sent to the provider (unique per tenant).</summary>
    public string OurReference { get; private set; } = string.Empty;
    /// <summary>Provider's id for the in-flight request (e.g. Daraja CheckoutRequestID), used to match callbacks.</summary>
    public string? ProviderRequestId { get; private set; }
    /// <summary>Provider's final receipt/transaction id, the idempotency key.</summary>
    public string? ProviderTransactionReference { get; private set; }
    public string? FailureReason { get; private set; }
    public Guid? LedgerJournalEntryId { get; private set; }
    public Guid InitiatedByUserId { get; private set; }
    public DateTimeOffset InitiatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int CallbackCount { get; private set; }
    public string? Narrative { get; private set; }

    public static PaymentTransaction Create(Guid id, Guid tenantId, PaymentKind kind, string provider, decimal amount, string counterparty, Guid? memberId, PaymentPurpose purpose, string ourReference, string? narrative, Guid byUser, DateTimeOffset now)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount) throw new DomainRuleException("payments.amount_invalid", "Amount must be positive with at most two decimals.");
        if (string.IsNullOrWhiteSpace(counterparty)) throw new DomainRuleException("payments.counterparty_required", "A phone number or bank account is required.");
        return new PaymentTransaction
        {
            Id = id, TenantId = tenantId, Kind = kind, Provider = provider, Status = PaymentStatus.Initiated, Amount = amount, Counterparty = counterparty.Trim(),
            MemberId = memberId, Purpose = purpose, OurReference = ourReference, Narrative = narrative, InitiatedByUserId = byUser, InitiatedAt = now,
        };
    }

    public void MarkPendingCallback(string providerRequestId) { ProviderRequestId = providerRequestId; Status = PaymentStatus.PendingCallback; }
    public void RecordCallback() => CallbackCount++;

    public void MarkSucceeded(string providerReference, Guid? journalId, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Succeeded) return;
        if (Status == PaymentStatus.Failed) throw new DomainRuleException("payments.already_failed", $"{OurReference} already failed; a success callback after failure needs manual reconciliation.");
        ProviderTransactionReference = providerReference; LedgerJournalEntryId = journalId; Status = PaymentStatus.Succeeded; CompletedAt = now;
    }

    public void MarkFailed(string reason, string? providerReference, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Succeeded) throw new DomainRuleException("payments.already_succeeded", $"{OurReference} already succeeded and cannot be failed.");
        FailureReason = reason; ProviderTransactionReference ??= providerReference; Status = PaymentStatus.Failed; CompletedAt = now;
    }

    public void MarkTimedOut(DateTimeOffset now)
    {
        if (Status is PaymentStatus.Succeeded or PaymentStatus.Failed) return;
        Status = PaymentStatus.TimedOut; CompletedAt = now; FailureReason ??= "No provider callback within the expected window; awaiting reconciliation.";
    }

    public bool IsFinal => Status is PaymentStatus.Succeeded or PaymentStatus.Failed;
}

/// <summary>
/// Idempotency ledger for provider callbacks: the unique index on (provider, provider transaction
/// reference) makes double-processing impossible, not merely unlikely (non-negotiable #5).
/// </summary>
public class ProcessedProviderTransaction : TenantEntity
{
    private ProcessedProviderTransaction() { }
    public string Provider { get; private set; } = string.Empty;
    public string ProviderTransactionReference { get; private set; } = string.Empty;
    public Guid? PaymentTransactionId { get; private set; }
    public string Outcome { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedProviderTransaction Create(Guid tenantId, string provider, string reference, Guid? transactionId, string outcome, DateTimeOffset now)
        => new() { Id = Ids.New(), TenantId = tenantId, Provider = provider, ProviderTransactionReference = reference, PaymentTransactionId = transactionId, Outcome = outcome, ProcessedAt = now };
}
