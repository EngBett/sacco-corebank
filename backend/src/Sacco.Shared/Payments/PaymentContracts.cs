namespace Sacco.Shared.Payments;

public enum PaymentKind { Collection = 1, Disbursement = 2 }
public enum PaymentStatus { Initiated = 1, PendingCallback = 2, Succeeded = 3, Failed = 4, TimedOut = 5 }
public enum PaymentPurposeType { SavingsDeposit = 1, LoanRepayment = 2, WithdrawalPayout = 3, Unallocated = 4 }
public enum ProviderTransactionState { Pending = 1, Succeeded = 2, Failed = 3, NotFound = 4 }

/// <summary>Well-known provider names. Banks are "Bank:&lt;code&gt;" so several banks can coexist behind one contract.</summary>
public static class ProviderNames
{
    public const string MPesa = "MPesa";
    public const string AirtelMoney = "AirtelMoney";
    public const string BankPrefix = "Bank:";
    public const string SandboxBank = "Bank:SANDBOX";
}

public sealed record CollectionRequest(Guid TenantId, string OurReference, string PhoneNumber, decimal Amount, string AccountReference, string Description);
public sealed record CollectionResult(bool Accepted, string? ProviderRequestId, string? FailureReason);
public sealed record DisbursementRequest(Guid TenantId, string OurReference, string Destination, decimal Amount, string Description);
public sealed record DisbursementResult(bool Accepted, string? ProviderRequestId, string? FailureReason);
public sealed record TransactionStatus(ProviderTransactionState State, string? ProviderTransactionReference, decimal? Amount, string? FailureReason);
public sealed record WebhookPayload(string Body, IReadOnlyDictionary<string, string> Headers, string? RemoteIp);

/// <summary>A provider callback normalised to what the platform needs. <see cref="ProviderTransactionReference"/> is the provider's own receipt/transaction id and is the idempotency key.</summary>
public sealed record ProviderEvent(
    string ProviderName,
    string ProviderTransactionReference,
    string? ProviderRequestId,
    string? AccountReference,
    ProviderTransactionState State,
    decimal Amount,
    string? PhoneNumber,
    string? FailureReason,
    DateTimeOffset OccurredAt);

public sealed record WebhookHandlingResult(bool Valid, ProviderEvent? Event, string? Error)
{
    public static WebhookHandlingResult Invalid(string error) => new(false, null, error);
    public static WebhookHandlingResult Ok(ProviderEvent e) => new(true, e, null);
}

/// <summary>
/// The one contract every payment provider implements (docs/integrations/payment-providers.md).
/// Domain code never calls a provider SDK directly. Every provider has a sandbox implementation.
/// </summary>
public interface IPaymentProvider
{
    string ProviderName { get; }
    bool IsSandbox { get; }
    /// <summary>STK/USSD push — member pays the SACCO.</summary>
    Task<CollectionResult> InitiateCollectionAsync(CollectionRequest request, CancellationToken ct);
    /// <summary>B2C — SACCO pays the member.</summary>
    Task<DisbursementResult> InitiateDisbursementAsync(DisbursementRequest request, CancellationToken ct);
    /// <summary>Reconciliation: ask the provider directly, used when a callback never arrives.</summary>
    Task<TransactionStatus> VerifyTransactionAsync(string providerRequestId, CancellationToken ct);
    /// <summary>Validates and parses a raw webhook. Must be pure: the caller enforces idempotency before any ledger posting.</summary>
    Task<WebhookHandlingResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken ct);
}
