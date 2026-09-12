using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Payments.Application;
using Sacco.Modules.Payments.Domain;
using Sacco.Modules.Payments.Persistence;
using Sacco.Modules.Savings.Application;
using Sacco.Modules.Savings.Domain;
using Sacco.Seed.Data;
using Sacco.Shared.Domain;
using Sacco.Shared.Payments;
using Sacco.Shared.Time;

namespace Sacco.Seed.Seeders;

/// <summary>
/// Provider fixtures for M-Pesa, Airtel Money and the sandbox bank: a successful collection (posted
/// to the ledger), a failed collection, a duplicate webhook delivery recorded as ignored, and a
/// successful disbursement of an approved withdrawal. These rows mirror what the sagas produce and
/// give the reconciliation screens something to show; the live flows are exercised by the
/// integration tests against the sandbox providers.
/// </summary>
public sealed class PaymentsSeeder(PaymentsDbContext db, PaymentFinalizer finalizer, SavingsService savings, IClock clock, ILogger<PaymentsSeeder> logger) : ISeeder
{
    public int Order => 40;

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Transactions.AnyAsync(ct)) { logger.LogInformation("  Payment fixtures already seeded"); return; }
        var now = clock.UtcNow;
        var created = 0;

        foreach (var (provider, prefix, depositMember, failMember) in new[]
        {
            (ProviderNames.MPesa, "SBXMP", "M00001", "M00002"),
            (ProviderNames.AirtelMoney, "SBXAM", "M00004", "M00005"),
            (ProviderNames.SandboxBank, "SBXBK", "M00006", "M00008"),
        })
        {
            // 1. Successful collection → ledger posting via the same finaliser the saga uses.
            var okRef = $"{prefix}-OK-0001";
            var ok = PaymentTransaction.Create(Ids.Deterministic($"pay:{provider}:ok"), DemoTenant.Id, PaymentKind.Collection, provider, 1_500m, Phone(depositMember), DemoTenant.MemberId(depositMember),
                new PaymentPurpose { Type = PaymentPurposeType.SavingsDeposit, AccountNumber = LedgerSeeder.FosaAccount(depositMember) }, $"COL-SEED-{prefix}-OK", $"{provider} deposit", DemoTenant.Users.Teller, now.AddDays(-3));
            ok.MarkPendingCallback($"{prefix}-REQ-SEED-OK");
            db.Transactions.Add(ok);
            await db.SaveChangesAsync(ct);
            await finalizer.ApplyAsync(ok.Id, new ProviderEvent(provider, okRef, ok.ProviderRequestId, null, ProviderTransactionState.Succeeded, 1_500m, Phone(depositMember), null, now.AddDays(-3)), ct);

            // 2. Duplicate delivery of the same receipt: the unique index makes it a no-op (proves idempotency).
            var dup = await finalizer.ApplyAsync(ok.Id, new ProviderEvent(provider, okRef, ok.ProviderRequestId, null, ProviderTransactionState.Succeeded, 1_500m, Phone(depositMember), null, now.AddDays(-3).AddMinutes(1)), ct);
            if (dup != FinalizeOutcome.AlreadyProcessed) throw new InvalidOperationException("Seed expected the duplicate webhook to be ignored.");

            // 3. Failed / timed-out collection.
            var failed = PaymentTransaction.Create(Ids.Deterministic($"pay:{provider}:failed"), DemoTenant.Id, PaymentKind.Collection, provider, 2_000m, Phone(failMember), DemoTenant.MemberId(failMember),
                new PaymentPurpose { Type = PaymentPurposeType.SavingsDeposit, AccountNumber = LedgerSeeder.FosaAccount(failMember) }, $"COL-SEED-{prefix}-FAIL", $"{provider} deposit", DemoTenant.Users.Teller, now.AddDays(-2));
            failed.MarkPendingCallback($"{prefix}-REQ-SEED-FAIL");
            db.Transactions.Add(failed);
            await db.SaveChangesAsync(ct);
            await finalizer.ApplyAsync(failed.Id, new ProviderEvent(provider, $"{prefix}-FAIL-0001", failed.ProviderRequestId, null, ProviderTransactionState.Failed, 2_000m, Phone(failMember), "Insufficient funds", now.AddDays(-2)), ct);

            var timedOut = PaymentTransaction.Create(Ids.Deterministic($"pay:{provider}:timeout"), DemoTenant.Id, PaymentKind.Collection, provider, 800m, Phone(failMember), DemoTenant.MemberId(failMember),
                new PaymentPurpose { Type = PaymentPurposeType.SavingsDeposit, AccountNumber = LedgerSeeder.FosaAccount(failMember) }, $"COL-SEED-{prefix}-TIMEOUT", $"{provider} deposit", DemoTenant.Users.Teller, now.AddDays(-1));
            timedOut.MarkPendingCallback($"{prefix}-REQ-SEED-TIMEOUT");
            timedOut.MarkTimedOut(now.AddDays(-1).AddMinutes(3));
            db.Transactions.Add(timedOut);
            await db.SaveChangesAsync(ct);
            created += 3;
        }

        // 4. Successful M-Pesa disbursement: an approved mobile-money withdrawal paid out.
        var withdrawal = await savings.RequestWithdrawalAsync(LedgerSeeder.FosaAccount("M00009"), 2_500m, PayoutChannel.MPesa, Phone("M00009"), "Send to M-Pesa (seeded payout)", DemoTenant.Users.Teller, ct);
        await savings.ApproveWithdrawalAsync(withdrawal.Id, DemoTenant.Users.BranchManager, ct);
        var payout = PaymentTransaction.Create(Ids.Deterministic("pay:MPesa:payout"), DemoTenant.Id, PaymentKind.Disbursement, ProviderNames.MPesa, 2_500m, Phone("M00009"), DemoTenant.MemberId("M00009"),
            new PaymentPurpose { Type = PaymentPurposeType.WithdrawalPayout, WithdrawalId = withdrawal.Id, AccountNumber = withdrawal.AccountNumber }, "DSB-SEED-SBXMP-OK", "Withdrawal payout", DemoTenant.Users.Teller, now.AddHours(-6));
        payout.MarkPendingCallback("SBXMP-REQ-SEED-B2C");
        db.Transactions.Add(payout);
        await db.SaveChangesAsync(ct);
        await finalizer.ApplyAsync(payout.Id, new ProviderEvent(ProviderNames.MPesa, "SBXMP-B2C-0001", payout.ProviderRequestId, null, ProviderTransactionState.Succeeded, 2_500m, Phone("M00009"), null, now.AddHours(-6)), ct);
        created++;

        logger.Created("Payment fixtures (per provider: success, duplicate-ignored, failed, timed-out; plus one payout)", created);
    }

    private static string Phone(string memberNo) => KenyanNames.Members.Single(m => m.MemberNumber == memberNo).Phone;
}
