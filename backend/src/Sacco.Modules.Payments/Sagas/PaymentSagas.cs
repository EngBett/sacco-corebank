using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sacco.Modules.Payments.Application;
using Sacco.Modules.Payments.Domain;
using Sacco.Modules.Payments.Persistence;
using Sacco.Modules.Payments.Providers;
using Sacco.Shared.Payments;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;
using Wolverine;

namespace Sacco.Modules.Payments.Sagas;

/// <summary>
/// STK/USSD-push collection: initiate with the provider → wait for the callback → post to the
/// ledger; if no callback arrives, verify with the provider and either finalise or park the
/// transaction as TimedOut for reconciliation. Never left in limbo (ADR 0003 scope).
/// </summary>
public sealed class CollectionSaga : Saga
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderRequestId { get; set; }
    public bool Finalised { get; set; }

    public static async Task<CollectionSaga> Start(StartCollection cmd, PaymentsDbContext db, PaymentProviderRegistry providers, TenantContext tenant, IClock clock, IMessageBus bus, IOptions<PaymentsSettings> settings, ILogger<CollectionSaga> logger, CancellationToken ct)
    {
        tenant.Set(cmd.TenantId, cmd.TenantSlug);
        var tx = await db.Transactions.FirstAsync(t => t.Id == cmd.Id, ct);
        var provider = providers.Resolve(tx.Provider);
        var result = await provider.InitiateCollectionAsync(new CollectionRequest(cmd.TenantId, tx.OurReference, tx.Counterparty, tx.Amount, tx.Purpose.AccountNumber ?? tx.Purpose.LoanNumber ?? tx.OurReference, tx.Narrative ?? "SACCO payment"), ct);
        var saga = new CollectionSaga { Id = cmd.Id, Provider = tx.Provider };
        if (!result.Accepted)
        {
            tx.MarkFailed(result.FailureReason ?? "Provider rejected the request", null, clock.UtcNow);
            saga.Finalised = true;
            saga.MarkCompleted();
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Collection {Reference} rejected by {Provider}: {Reason}", tx.OurReference, tx.Provider, result.FailureReason);
            return saga;
        }
        tx.MarkPendingCallback(result.ProviderRequestId!);
        saga.ProviderRequestId = result.ProviderRequestId;
        await db.SaveChangesAsync(ct);
        await bus.ScheduleAsync(new CollectionTimeout(cmd.Id, cmd.TenantId, cmd.TenantSlug), TimeSpan.FromMinutes(Math.Max(1, settings.Value.CallbackTimeoutMinutes)));
        if (provider.IsSandbox && settings.Value.SandboxAutoCallbackSeconds > 0)
            await bus.ScheduleAsync(new SimulateCollectionCallback(cmd.Id, cmd.TenantId, cmd.TenantSlug), TimeSpan.FromSeconds(settings.Value.SandboxAutoCallbackSeconds));
        return saga;
    }

    public async Task Handle(CollectionCallbackReceived evt, PaymentFinalizer finalizer, TenantContext tenant, CancellationToken ct)
    {
        tenant.Set(evt.TenantId, evt.TenantSlug);
        var outcome = await finalizer.ApplyAsync(evt.Id, evt.Event, ct);
        if (outcome != FinalizeOutcome.Unmatched) { Finalised = true; MarkCompleted(); }
    }

    public async Task Handle(CollectionTimeout timeout, PaymentsDbContext db, PaymentProviderRegistry providers, PaymentFinalizer finalizer, TenantContext tenant, IClock clock, ILogger<CollectionSaga> logger, CancellationToken ct)
    {
        tenant.Set(timeout.TenantId, timeout.TenantSlug);
        if (Finalised) { MarkCompleted(); return; }
        var tx = await db.Transactions.FirstAsync(t => t.Id == timeout.Id, ct);
        if (tx.IsFinal) { Finalised = true; MarkCompleted(); return; }

        var status = ProviderRequestId is null ? new TransactionStatus(ProviderTransactionState.NotFound, null, null, null) : await providers.Resolve(Provider).VerifyTransactionAsync(ProviderRequestId, ct);
        switch (status.State)
        {
            case ProviderTransactionState.Succeeded or ProviderTransactionState.Failed:
                await finalizer.ApplyAsync(timeout.Id, new ProviderEvent(Provider, status.ProviderTransactionReference ?? $"VERIFY-{ProviderRequestId}", ProviderRequestId, null, status.State, status.Amount ?? tx.Amount, tx.Counterparty, status.FailureReason, clock.UtcNow), ct);
                Finalised = true; MarkCompleted();
                break;
            default:
                tx.MarkTimedOut(clock.UtcNow);
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Collection {Reference} timed out awaiting {Provider}; parked for reconciliation", tx.OurReference, Provider);
                MarkCompleted();
                break;
        }
    }

    public async Task Handle(SimulateCollectionCallback cmd, PaymentProviderRegistry providers, WebhookProcessor processor, TenantContext tenant, CancellationToken ct)
    {
        tenant.Set(cmd.TenantId, cmd.TenantSlug);
        if (Finalised || ProviderRequestId is null) return;
        if (providers.Resolve(Provider) is SandboxProviderBase sandbox && sandbox.BuildCallback(ProviderRequestId) is { } callback)
            await processor.ProcessSandboxCallbackAsync(Provider, callback, ct);
    }
}

/// <summary>B2C payout for an approved withdrawal. Same shape as the collection saga.</summary>
public sealed class DisbursementSaga : Saga
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderRequestId { get; set; }
    public bool Finalised { get; set; }

    public static async Task<DisbursementSaga> Start(StartDisbursement cmd, PaymentsDbContext db, PaymentProviderRegistry providers, TenantContext tenant, IClock clock, IMessageBus bus, IOptions<PaymentsSettings> settings, ILogger<DisbursementSaga> logger, CancellationToken ct)
    {
        tenant.Set(cmd.TenantId, cmd.TenantSlug);
        var tx = await db.Transactions.FirstAsync(t => t.Id == cmd.Id, ct);
        var provider = providers.Resolve(tx.Provider);
        var result = await provider.InitiateDisbursementAsync(new DisbursementRequest(cmd.TenantId, tx.OurReference, tx.Counterparty, tx.Amount, tx.Narrative ?? "SACCO payout"), ct);
        var saga = new DisbursementSaga { Id = cmd.Id, Provider = tx.Provider };
        if (!result.Accepted)
        {
            tx.MarkFailed(result.FailureReason ?? "Provider rejected the request", null, clock.UtcNow);
            saga.Finalised = true; saga.MarkCompleted();
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Disbursement {Reference} rejected by {Provider}: {Reason}", tx.OurReference, tx.Provider, result.FailureReason);
            return saga;
        }
        tx.MarkPendingCallback(result.ProviderRequestId!);
        saga.ProviderRequestId = result.ProviderRequestId;
        await db.SaveChangesAsync(ct);
        await bus.ScheduleAsync(new DisbursementTimeout(cmd.Id, cmd.TenantId, cmd.TenantSlug), TimeSpan.FromMinutes(Math.Max(1, settings.Value.CallbackTimeoutMinutes)));
        if (provider.IsSandbox && settings.Value.SandboxAutoCallbackSeconds > 0)
            await bus.ScheduleAsync(new SimulateDisbursementCallback(cmd.Id, cmd.TenantId, cmd.TenantSlug), TimeSpan.FromSeconds(settings.Value.SandboxAutoCallbackSeconds));
        return saga;
    }

    public async Task Handle(DisbursementCallbackReceived evt, PaymentFinalizer finalizer, TenantContext tenant, CancellationToken ct)
    {
        tenant.Set(evt.TenantId, evt.TenantSlug);
        var outcome = await finalizer.ApplyAsync(evt.Id, evt.Event, ct);
        if (outcome != FinalizeOutcome.Unmatched) { Finalised = true; MarkCompleted(); }
    }

    public async Task Handle(DisbursementTimeout timeout, PaymentsDbContext db, PaymentProviderRegistry providers, PaymentFinalizer finalizer, TenantContext tenant, IClock clock, ILogger<DisbursementSaga> logger, CancellationToken ct)
    {
        tenant.Set(timeout.TenantId, timeout.TenantSlug);
        if (Finalised) { MarkCompleted(); return; }
        var tx = await db.Transactions.FirstAsync(t => t.Id == timeout.Id, ct);
        if (tx.IsFinal) { Finalised = true; MarkCompleted(); return; }
        var status = ProviderRequestId is null ? new TransactionStatus(ProviderTransactionState.NotFound, null, null, null) : await providers.Resolve(Provider).VerifyTransactionAsync(ProviderRequestId, ct);
        if (status.State is ProviderTransactionState.Succeeded or ProviderTransactionState.Failed)
        {
            await finalizer.ApplyAsync(timeout.Id, new ProviderEvent(Provider, status.ProviderTransactionReference ?? $"VERIFY-{ProviderRequestId}", ProviderRequestId, null, status.State, status.Amount ?? tx.Amount, tx.Counterparty, status.FailureReason, clock.UtcNow), ct);
            Finalised = true; MarkCompleted();
        }
        else
        {
            tx.MarkTimedOut(clock.UtcNow);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Disbursement {Reference} timed out awaiting {Provider}; parked for reconciliation", tx.OurReference, Provider);
            MarkCompleted();
        }
    }

    public async Task Handle(SimulateDisbursementCallback cmd, PaymentProviderRegistry providers, WebhookProcessor processor, TenantContext tenant, CancellationToken ct)
    {
        tenant.Set(cmd.TenantId, cmd.TenantSlug);
        if (Finalised || ProviderRequestId is null) return;
        if (providers.Resolve(Provider) is SandboxProviderBase sandbox && sandbox.BuildCallback(ProviderRequestId) is { } callback)
            await processor.ProcessSandboxCallbackAsync(Provider, callback, ct);
    }
}
