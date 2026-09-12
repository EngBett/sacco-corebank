using System.Collections.Concurrent;
using System.Text.Json;
using Sacco.Shared.Payments;

namespace Sacco.Modules.Payments.Providers;

/// <summary>
/// Deterministic in-memory sandbox used by default in local, CI and staging. Behaviour is driven by
/// the counterparty so demos and tests can force outcomes without a real provider:
///   • phone/account ending "97" → the provider rejects the request at initiation
///   • ending "98" → accepted, then the callback (or verification) reports failure ("Insufficient funds")
///   • anything else → accepted, then succeeds
/// Callbacks are delivered by POSTing <see cref="SandboxCallback"/> JSON to /api/payments/webhooks/{tenant}/{provider}
/// (the simulate endpoint does this for you), or automatically when Payments:Sandbox:AutoCallbackSeconds &gt; 0.
/// </summary>
public abstract class SandboxProviderBase(string providerName, string referencePrefix) : IPaymentProvider
{
    private sealed record Pending(string OurReference, decimal Amount, string Counterparty, bool WillFail, DateTimeOffset At);
    private readonly ConcurrentDictionary<string, Pending> _requests = new();
    private int _sequence;

    public const string RejectSuffix = "97";
    public const string FailSuffix = "98";
    public string ProviderName { get; } = providerName;
    public bool IsSandbox => true;

    public Task<CollectionResult> InitiateCollectionAsync(CollectionRequest r, CancellationToken ct)
    {
        if (r.PhoneNumber.EndsWith(RejectSuffix)) return Task.FromResult(new CollectionResult(false, null, $"{ProviderName} sandbox: request rejected (test number)"));
        var id = NextRequestId();
        _requests[id] = new Pending(r.OurReference, r.Amount, r.PhoneNumber, r.PhoneNumber.EndsWith(FailSuffix), DateTimeOffset.UtcNow);
        return Task.FromResult(new CollectionResult(true, id, null));
    }

    public Task<DisbursementResult> InitiateDisbursementAsync(DisbursementRequest r, CancellationToken ct)
    {
        if (r.Destination.EndsWith(RejectSuffix)) return Task.FromResult(new DisbursementResult(false, null, $"{ProviderName} sandbox: destination rejected (test number)"));
        var id = NextRequestId();
        _requests[id] = new Pending(r.OurReference, r.Amount, r.Destination, r.Destination.EndsWith(FailSuffix), DateTimeOffset.UtcNow);
        return Task.FromResult(new DisbursementResult(true, id, null));
    }

    public Task<TransactionStatus> VerifyTransactionAsync(string providerRequestId, CancellationToken ct)
    {
        if (!_requests.TryGetValue(providerRequestId, out var p)) return Task.FromResult(new TransactionStatus(ProviderTransactionState.NotFound, null, null, "Unknown request"));
        return Task.FromResult(p.WillFail
            ? new TransactionStatus(ProviderTransactionState.Failed, ReceiptFor(providerRequestId), p.Amount, "Insufficient funds")
            : new TransactionStatus(ProviderTransactionState.Succeeded, ReceiptFor(providerRequestId), p.Amount, null));
    }

    public Task<WebhookHandlingResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken ct)
    {
        SandboxCallback? cb;
        try { cb = JsonSerializer.Deserialize<SandboxCallback>(payload.Body, JsonOptions); }
        catch (JsonException ex) { return Task.FromResult(WebhookHandlingResult.Invalid($"Malformed sandbox callback: {ex.Message}")); }
        if (cb is null || string.IsNullOrWhiteSpace(cb.ProviderTransactionReference)) return Task.FromResult(WebhookHandlingResult.Invalid("providerTransactionReference is required"));
        var state = cb.Result.Equals("Success", StringComparison.OrdinalIgnoreCase) ? ProviderTransactionState.Succeeded : ProviderTransactionState.Failed;
        return Task.FromResult(WebhookHandlingResult.Ok(new ProviderEvent(ProviderName, cb.ProviderTransactionReference, cb.ProviderRequestId, cb.AccountReference, state, cb.Amount, cb.PhoneNumber, cb.FailureReason, DateTimeOffset.UtcNow)));
    }

    /// <summary>Builds the callback the sandbox would send for an in-flight request (used by the simulate endpoint and auto-callbacks).</summary>
    public SandboxCallback? BuildCallback(string providerRequestId, bool? forceSuccess = null)
    {
        if (!_requests.TryGetValue(providerRequestId, out var p)) return null;
        var success = forceSuccess ?? !p.WillFail;
        return new SandboxCallback(providerRequestId, ReceiptFor(providerRequestId), success ? "Success" : "Failed", p.Amount, p.Counterparty, null, success ? null : "Insufficient funds");
    }

    private string NextRequestId() => $"{referencePrefix}-REQ-{Interlocked.Increment(ref _sequence):D6}-{Guid.NewGuid():N}"[..40];
    private string ReceiptFor(string requestId) => $"{referencePrefix}{requestId.GetHashCode():X8}".ToUpperInvariant();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>Sandbox webhook body. Real providers have their own shapes; each provider class parses its own.</summary>
public sealed record SandboxCallback(string? ProviderRequestId, string ProviderTransactionReference, string Result, decimal Amount, string? PhoneNumber, string? AccountReference, string? FailureReason);

public sealed class SandboxMpesaProvider() : SandboxProviderBase(ProviderNames.MPesa, "SBXMP");
public sealed class SandboxAirtelMoneyProvider() : SandboxProviderBase(ProviderNames.AirtelMoney, "SBXAM");
public sealed class SandboxBankProvider() : SandboxProviderBase(ProviderNames.SandboxBank, "SBXBK");
