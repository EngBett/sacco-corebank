using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Payments.Domain;
using Sacco.Modules.Payments.Persistence;
using Sacco.Modules.Payments.Sagas;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Lending;
using Sacco.Shared.Members;
using Sacco.Shared.Payments;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;
using Wolverine;

namespace Sacco.Modules.Payments.Application;

public sealed class PaymentService(PaymentsDbContext db, PaymentProviderRegistry providers, ISavingsService savings, IMemberDirectory members, ITenantContext tenant, IClock clock, IAuditLogger audit, IMessageBus bus)
{
    private static string NewReference(PaymentKind kind) => $"{(kind == PaymentKind.Collection ? "COL" : "DSB")}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..28].ToUpperInvariant();

    /// <summary>Starts a member-initiated collection (deposit or loan repayment) via STK/USSD push.</summary>
    public async Task<PaymentTransaction> InitiateCollectionAsync(string provider, string phoneNumber, decimal amount, PaymentPurposeType purposeType, string? accountNumber, string? loanNumber, string? narrative, Guid byUser, CancellationToken ct)
    {
        providers.Resolve(provider); // validates the name
        var purpose = purposeType switch
        {
            PaymentPurposeType.SavingsDeposit when !string.IsNullOrWhiteSpace(accountNumber) => new PaymentPurpose { Type = purposeType, AccountNumber = accountNumber },
            PaymentPurposeType.LoanRepayment when !string.IsNullOrWhiteSpace(loanNumber) => new PaymentPurpose { Type = purposeType, LoanNumber = loanNumber },
            _ => throw new DomainRuleException("payments.purpose_invalid", "A collection needs a savings account number or a loan number."),
        };
        var member = await members.FindByPhoneAsync(phoneNumber, ct);
        var tx = PaymentTransaction.Create(Ids.New(), tenant.TenantId, PaymentKind.Collection, provider, amount, phoneNumber, member?.Id, purpose, NewReference(PaymentKind.Collection), narrative, byUser, clock.UtcNow);
        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("payments.collection.initiated", nameof(PaymentTransaction), tx.Id.ToString(), byUser, $$"""{"reference":"{{tx.OurReference}}","provider":"{{provider}}","amount":{{amount}},"purpose":"{{purposeType}}"}"""), ct);
        await bus.InvokeAsync(new StartCollection(tx.Id, tenant.TenantId, tenant.TenantSlug), ct);
        return await db.Transactions.AsNoTracking().FirstAsync(t => t.Id == tx.Id, ct);
    }

    /// <summary>Pays out an approved mobile-money/bank withdrawal through the provider (B2C).</summary>
    public async Task<PaymentTransaction> InitiateWithdrawalPayoutAsync(Guid withdrawalId, string? providerOverride, Guid byUser, CancellationToken ct)
    {
        var w = await savings.GetWithdrawalForPayoutAsync(withdrawalId, ct) ?? throw new NotFoundException("Withdrawal", withdrawalId);
        if (w.IsPaid) throw new ConflictException("payments.withdrawal_already_paid", "This withdrawal has already been paid.");
        if (!w.IsApproved) throw new DomainRuleException("payments.withdrawal_not_approved", "Only approved withdrawals can be paid out.");
        var provider = providerOverride ?? w.Channel switch
        {
            "MPesa" => ProviderNames.MPesa,
            "AirtelMoney" => ProviderNames.AirtelMoney,
            "BankTransfer" => ProviderNames.SandboxBank,
            _ => throw new DomainRuleException("payments.withdrawal_channel", $"Withdrawal channel {w.Channel} is not paid through a provider."),
        };
        providers.Resolve(provider);
        if (await db.Transactions.AnyAsync(t => t.Purpose.WithdrawalId == withdrawalId && (t.Status == PaymentStatus.Initiated || t.Status == PaymentStatus.PendingCallback || t.Status == PaymentStatus.Succeeded), ct))
            throw new ConflictException("payments.payout_in_progress", "A payout for this withdrawal is already in progress or complete.");

        var tx = PaymentTransaction.Create(Ids.New(), tenant.TenantId, PaymentKind.Disbursement, provider, w.Amount, w.Destination ?? throw new DomainRuleException("payments.destination_required", "The withdrawal has no payout destination."),
            w.MemberId, new PaymentPurpose { Type = PaymentPurposeType.WithdrawalPayout, WithdrawalId = withdrawalId, AccountNumber = w.AccountNumber }, NewReference(PaymentKind.Disbursement), $"Withdrawal payout {w.AccountNumber}", byUser, clock.UtcNow);
        db.Transactions.Add(tx);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("payments.disbursement.initiated", nameof(PaymentTransaction), tx.Id.ToString(), byUser, $$"""{"reference":"{{tx.OurReference}}","provider":"{{provider}}","amount":{{w.Amount}},"withdrawalId":"{{withdrawalId}}"}"""), ct);
        await bus.InvokeAsync(new StartDisbursement(tx.Id, tenant.TenantId, tenant.TenantSlug), ct);
        return await db.Transactions.AsNoTracking().FirstAsync(t => t.Id == tx.Id, ct);
    }

    /// <summary>Manual reconciliation: ask the provider now and apply the answer.</summary>
    public async Task<PaymentTransaction> ReconcileAsync(Guid id, PaymentFinalizer finalizer, CancellationToken ct)
    {
        var tx = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Payment transaction", id);
        if (tx.IsFinal) return tx;
        if (tx.ProviderRequestId is null) throw new DomainRuleException("payments.not_initiated", "The provider never accepted this request; nothing to reconcile.");
        var status = await providers.Resolve(tx.Provider).VerifyTransactionAsync(tx.ProviderRequestId, ct);
        if (status.State is ProviderTransactionState.Succeeded or ProviderTransactionState.Failed)
            await finalizer.ApplyAsync(id, new ProviderEvent(tx.Provider, status.ProviderTransactionReference ?? $"VERIFY-{tx.ProviderRequestId}", tx.ProviderRequestId, null, status.State, status.Amount ?? tx.Amount, tx.Counterparty, status.FailureReason, clock.UtcNow), ct);
        return await db.Transactions.AsNoTracking().FirstAsync(t => t.Id == id, ct);
    }
}

/// <summary>Turns a raw provider webhook into saga messages, or handles unsolicited credits directly.</summary>
public sealed class WebhookProcessor(PaymentsDbContext db, PaymentProviderRegistry providers, PaymentFinalizer finalizer, ITenantContext tenant, ISavingsService savings, IMessageBus bus, IClock clock, ILogger<WebhookProcessor> logger)
{
    public sealed record Result(bool Accepted, string Message, Guid? TransactionId);

    public async Task<Result> ProcessAsync(string providerName, WebhookPayload payload, CancellationToken ct)
    {
        var provider = providers.Resolve(providerName);
        var parsed = await provider.HandleWebhookAsync(payload, ct);
        if (!parsed.Valid || parsed.Event is null) return new Result(false, parsed.Error ?? "Invalid payload", null);
        return await RouteAsync(parsed.Event, ct);
    }

    public async Task<Result> ProcessSandboxCallbackAsync(string providerName, Providers.SandboxCallback callback, CancellationToken ct)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(callback, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        return await ProcessAsync(providerName, new WebhookPayload(body, new Dictionary<string, string>(), "127.0.0.1"), ct);
    }

    private async Task<Result> RouteAsync(ProviderEvent e, CancellationToken ct)
    {
        PaymentTransaction? tx = null;
        if (!string.IsNullOrEmpty(e.ProviderRequestId))
            tx = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Provider == e.ProviderName && t.ProviderRequestId == e.ProviderRequestId, ct);

        if (tx is not null)
        {
            // Late or duplicate callbacks for a transaction the saga already closed (final or timed out) are applied
            // directly: the finaliser is idempotent and there is no saga instance left to route through.
            if (tx.IsFinal || tx.Status == PaymentStatus.TimedOut)
            {
                var outcome = await finalizer.ApplyAsync(tx.Id, e, ct);
                return new Result(true, outcome.ToString(), tx.Id);
            }
            // Inline so the provider's HTTP response reflects our processing; a retry is idempotent.
            if (tx.Kind == PaymentKind.Collection) await bus.InvokeAsync(new CollectionCallbackReceived(tx.Id, tenant.TenantId, tenant.TenantSlug, e), ct);
            else await bus.InvokeAsync(new DisbursementCallbackReceived(tx.Id, tenant.TenantId, tenant.TenantSlug, e), ct);
            return new Result(true, "Processed", tx.Id);
        }

        // Unsolicited credit (e.g. C2B paybill): the account reference names the destination.
        if (e.State == ProviderTransactionState.Succeeded && !string.IsNullOrWhiteSpace(e.AccountReference))
        {
            var reference = e.AccountReference.Trim().ToUpperInvariant();
            var purpose = reference.StartsWith("LN-") ? new PaymentPurpose { Type = PaymentPurposeType.LoanRepayment, LoanNumber = reference } : new PaymentPurpose { Type = PaymentPurposeType.SavingsDeposit, AccountNumber = reference };
            var created = PaymentTransaction.Create(Ids.New(), tenant.TenantId, PaymentKind.Collection, e.ProviderName, e.Amount, e.PhoneNumber ?? "unknown", null, purpose, $"C2B-{e.ProviderTransactionReference}", $"{e.ProviderName} unsolicited credit", SystemActors.System, clock.UtcNow);
            created.MarkPendingCallback(e.ProviderTransactionReference);
            db.Transactions.Add(created);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return new Result(true, "Duplicate delivery ignored", null); }
            try
            {
                var outcome = await finalizer.ApplyAsync(created.Id, e, ct);
                return new Result(true, outcome.ToString(), created.Id);
            }
            catch (DomainException ex)
            {
                logger.LogWarning("Unsolicited {Provider} credit {Ref} could not be applied ({Reason}); parking in suspense", e.ProviderName, e.ProviderTransactionReference, ex.Message);
                // The finaliser claimed the provider reference before the posting failed; release that claim so the
                // suspense parking (which re-claims it with outcome "Unmatched") can proceed.
                var claim = await db.Processed.FirstOrDefaultAsync(p => p.Provider == e.ProviderName && p.ProviderTransactionReference == e.ProviderTransactionReference && p.PaymentTransactionId == created.Id, ct);
                if (claim is not null) db.Processed.Remove(claim);
                db.Transactions.Remove(created);
                await db.SaveChangesAsync(ct);
                await finalizer.ParkUnmatchedAsync(e, ct);
                return new Result(true, "Parked in suspense", null);
            }
        }

        await finalizer.ParkUnmatchedAsync(e, ct);
        return new Result(true, "Unmatched; recorded for reconciliation", null);
    }
}
