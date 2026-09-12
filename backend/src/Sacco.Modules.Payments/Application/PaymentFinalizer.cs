using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Payments.Domain;
using Sacco.Modules.Payments.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Payments;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Payments.Application;

public enum FinalizeOutcome { Applied, AlreadyProcessed, Unmatched }

/// <summary>
/// Applies a provider outcome to a transaction exactly once. The processed-transactions insert
/// happens first, inside the same unit of work as the status change, so a duplicate delivery is
/// detected by the unique index before any ledger posting.
/// </summary>
public sealed class PaymentFinalizer(PaymentsDbContext db, ISavingsService savings, ILendingService lending, ILedgerService ledger, ITenantContext tenant, IClock clock, IAuditLogger audit, ILogger<PaymentFinalizer> logger)
{
    public async Task<FinalizeOutcome> ApplyAsync(Guid transactionId, ProviderEvent e, CancellationToken ct)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId, ct) ?? throw new NotFoundException("Payment transaction", transactionId);
        tx.RecordCallback();

        if (await db.Processed.AsNoTracking().AnyAsync(p => p.Provider == e.ProviderName && p.ProviderTransactionReference == e.ProviderTransactionReference, ct))
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Duplicate {Provider} callback {Reference} for {OurReference} ignored", e.ProviderName, e.ProviderTransactionReference, tx.OurReference);
            return FinalizeOutcome.AlreadyProcessed;
        }
        if (tx.IsFinal)
        {
            await db.SaveChangesAsync(ct);
            return FinalizeOutcome.AlreadyProcessed;
        }

        // Claim the provider reference first. A concurrent duplicate hits the unique index here.
        db.Processed.Add(ProcessedProviderTransaction.Create(tenant.TenantId, e.ProviderName, e.ProviderTransactionReference, tx.Id, e.State.ToString(), clock.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return FinalizeOutcome.AlreadyProcessed;
        }

        if (e.State == ProviderTransactionState.Succeeded)
        {
            var journalId = await PostAsync(tx, e, ct);
            tx.MarkSucceeded(e.ProviderTransactionReference, journalId, clock.UtcNow);
            await audit.RecordAsync(new AuditEvent("payments.succeeded", nameof(PaymentTransaction), tx.Id.ToString(), tx.InitiatedByUserId, $$"""{"reference":"{{tx.OurReference}}","provider":"{{e.ProviderName}}","providerRef":"{{e.ProviderTransactionReference}}","amount":{{tx.Amount}}}"""), ct);
        }
        else
        {
            tx.MarkFailed(e.FailureReason ?? "Provider reported failure", e.ProviderTransactionReference, clock.UtcNow);
            if (tx.Purpose.Type == PaymentPurposeType.WithdrawalPayout && tx.Purpose.WithdrawalId is Guid wid)
                await savings.FailWithdrawalPayoutAsync(wid, tx.FailureReason!, ct);
            await audit.RecordAsync(new AuditEvent("payments.failed", nameof(PaymentTransaction), tx.Id.ToString(), tx.InitiatedByUserId, $$"""{"reference":"{{tx.OurReference}}","reason":"{{(tx.FailureReason ?? "").Replace("\"", "'")}}"}"""), ct);
        }
        await db.SaveChangesAsync(ct);
        return FinalizeOutcome.Applied;
    }

    private async Task<Guid?> PostAsync(PaymentTransaction tx, ProviderEvent e, CancellationToken ct)
    {
        var channel = ChannelFor(tx.Provider);
        switch (tx.Purpose.Type)
        {
            case PaymentPurposeType.SavingsDeposit:
                return (await savings.DepositAsync(new DepositCommand(tx.Purpose.AccountNumber!, tx.Amount, channel, e.ProviderTransactionReference, $"{tx.Provider} deposit {e.ProviderTransactionReference}", tx.InitiatedByUserId), ct)).JournalEntryId;
            case PaymentPurposeType.LoanRepayment:
                return (await lending.RepayAsync(new RepaymentCommand(tx.Purpose.LoanNumber!, tx.Amount, RepaymentChannelFor(tx.Provider), e.ProviderTransactionReference, $"{tx.Provider} repayment {e.ProviderTransactionReference}", tx.InitiatedByUserId), ct)).JournalEntryId;
            case PaymentPurposeType.WithdrawalPayout:
                await savings.CompleteWithdrawalPayoutAsync(tx.Purpose.WithdrawalId!.Value, e.ProviderTransactionReference, tx.InitiatedByUserId, ct);
                return null;
            default:
                return null;
        }
    }

    /// <summary>An unsolicited provider credit (e.g. C2B paybill) that matched no account: park it in suspense so cash and the settlement account still reconcile.</summary>
    public async Task ParkUnmatchedAsync(ProviderEvent e, CancellationToken ct)
    {
        if (await db.Processed.AsNoTracking().AnyAsync(p => p.Provider == e.ProviderName && p.ProviderTransactionReference == e.ProviderTransactionReference, ct)) return;
        db.Processed.Add(ProcessedProviderTransaction.Create(tenant.TenantId, e.ProviderName, e.ProviderTransactionReference, null, "Unmatched", clock.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return; }
        if (e.State == ProviderTransactionState.Succeeded && e.Amount > 0)
        {
            var settlement = SettlementGlFor(e.ProviderName);
            await ledger.PostAsync(new PostingRequest($"PAY-UNMATCHED:{e.ProviderName}:{e.ProviderTransactionReference}", $"Unmatched {e.ProviderName} receipt {e.ProviderTransactionReference} (ref '{e.AccountReference}') parked in suspense", clock.Today, "Payments", SystemActors.System,
            [
                new PostingLine(settlement, Segment.Fosa, EntryDirection.Debit, e.Amount, Narrative: "Unmatched receipt"),
                new PostingLine("2500", Segment.Fosa, EntryDirection.Credit, e.Amount, Narrative: "Awaiting identification"),
            ]), ct);
        }
    }

    public static DepositChannel ChannelFor(string provider) => provider switch
    {
        ProviderNames.MPesa => DepositChannel.MPesa,
        ProviderNames.AirtelMoney => DepositChannel.AirtelMoney,
        _ => DepositChannel.BankTransfer,
    };
    public static RepaymentChannel RepaymentChannelFor(string provider) => provider switch
    {
        ProviderNames.MPesa => RepaymentChannel.MPesa,
        ProviderNames.AirtelMoney => RepaymentChannel.AirtelMoney,
        _ => RepaymentChannel.BankTransfer,
    };
    public static string SettlementGlFor(string provider) => provider switch
    {
        ProviderNames.MPesa => "1030",
        ProviderNames.AirtelMoney => "1040",
        _ => "1020",
    };
}
