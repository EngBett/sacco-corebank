using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Savings.Application;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Domain;
using Sacco.Shared.Savings;

namespace Sacco.Seed.Seeders;

/// <summary>
/// Products (FOSA current, BOSA deposits, shares, two fixed-deposit terms) and the product-level
/// account rows for every seeded ledger account. Runs after the ledger seeder so balances exist.
/// </summary>
public sealed class SavingsSeeder(SavingsDbContext db, SavingsService savings, ILogger<SavingsSeeder> logger) : ISeeder
{
    public int Order => 18; // products before ledger accounts are opened; account rows are added by LedgerSeeder via SavingsService

    public const string FosaCurrent = LedgerSeeder.ProductFosaCurrent;   // FOSA-CUR
    public const string BosaDeposit = LedgerSeeder.ProductBosaSavings;   // BOSA-DEP
    public const string Shares = LedgerSeeder.ProductShares;             // SHARES
    public const string FixedDeposit6 = "FD-6M";
    public const string FixedDeposit12 = "FD-12M";

    private sealed record Def(string Code, string Name, string Description, ProductKind Kind, Segment Segment, string ControlGl, string Suffix,
        decimal MinOpening, decimal MinBalance, bool Withdrawals, int NoticeDays, decimal TellerLimit, decimal Fee, string? FeeGl, int RateBps, string? InterestGl, int? Term);

    private static readonly Def[] Products =
    [
        new(FosaCurrent, "FOSA Current Account", "On-demand transactional account; cash, M-Pesa, Airtel Money, bank.", ProductKind.FosaCurrent, Segment.Fosa, Coa.FosaSavingsControl, "FO",
            500m, 200m, true, 0, 50_000m, 50m, Coa.FosaFeesAndCharges, 0, null, null),
        new(BosaDeposit, "BOSA Member Deposits", "Non-withdrawable monthly deposits that determine loan eligibility. 60 days' notice to withdraw; normally refunded on exit.", ProductKind.BosaDeposit, Segment.Bosa, Coa.BosaMemberDepositsControl, "SV",
            1_000m, 0m, true, 60, 0m, 0m, null, 0, null, null),
        new(Shares, "Share Capital", "Minimum share capital for full membership; earns dividends; not withdrawable.", ProductKind.Shares, Segment.Bosa, Coa.ShareCapitalControl, "SH",
            10_000m, 10_000m, false, 0, 0m, 0m, null, 0, null, null),
        new(FixedDeposit6, "Fixed Deposit — 6 months", "8% p.a. simple interest paid at maturity.", ProductKind.FixedDeposit, Segment.Bosa, Coa.BosaFixedDepositsControl, "FD",
            20_000m, 0m, false, 0, 0m, 0m, null, 800, Coa.BosaInterestExpense, 6),
        new(FixedDeposit12, "Fixed Deposit — 12 months", "10% p.a. simple interest paid at maturity.", ProductKind.FixedDeposit, Segment.Bosa, Coa.BosaFixedDepositsControl, "FD",
            20_000m, 0m, false, 0, 0m, 0m, null, 1000, Coa.BosaInterestExpense, 12),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.Products.Select(p => p.Code).ToHashSetAsync(ct);
        var created = 0;
        foreach (var d in Products)
        {
            if (existing.Contains(d.Code)) continue;
            db.Products.Add(SavingsProduct.Create(Ids.Deterministic($"product:{DemoTenant.Slug}:{d.Code}"), DemoTenant.Id, d.Code, d.Name, d.Description, d.Kind, d.Segment, d.ControlGl, d.Suffix,
                d.MinOpening, d.MinBalance, d.Withdrawals, d.NoticeDays, d.TellerLimit, d.Fee, d.FeeGl, d.RateBps, d.InterestGl, d.Term));
            created++;
        }
        await db.SaveChangesAsync(ct);
        logger.Created("Savings products", created);
    }
}

/// <summary>Post-ledger scenarios: fixed deposits, withdrawals in each state, and a dividend declaration awaiting approval.</summary>
public sealed class SavingsScenarioSeeder(SavingsDbContext db, SavingsService savings, DividendService dividends, Sacco.Modules.Members.Application.MemberService members, Sacco.Shared.Members.IMemberDirectory directory, ILogger<SavingsScenarioSeeder> logger) : ISeeder
{
    public int Order => 25;

    public async Task SeedAsync(CancellationToken ct)
    {
        // A member suspended *after* joining and transacting (accounts stay on the books, frozen for new activity).
        foreach (var p in KenyanNames.Members.Where(m => m.Kyc == "Suspended"))
        {
            var id = DemoTenant.MemberId(p.MemberNumber);
            if ((await directory.FindAsync(id, ct))?.KycStatus == Sacco.Shared.Members.KycStatus.Verified)
                await members.SuspendAsync(id, "Cheque returned unpaid twice; pending investigation", DemoTenant.Users.BranchManager, ct);
        }

        // Top up the FOSA accounts the scenarios draw on (idempotent: the receipt reference is the journal key).
        foreach (var (memberNo, amount) in new[] { ("M00003", 25_000m), ("M00009", 25_000m), ("M00015", 15_000m) })
        {
            try
            {
                await savings.DepositAsync(new DepositCommand(LedgerSeeder.FosaAccount(memberNo), amount, DepositChannel.Cash, $"SEED-TOPUP:{memberNo}", "Salary cash deposit", DemoTenant.Users.Teller), ct);
            }
            catch (ConflictException) { /* already seeded */ }
        }

        var created = 0;
        // Two fixed deposits funded from FOSA current accounts (M00003 and M00009 have healthy FOSA balances).
        foreach (var (memberNo, product, principal) in new[] { ("M00003", SavingsSeeder.FixedDeposit12, 20_000m), ("M00009", SavingsSeeder.FixedDeposit6, 20_000m) })
        {
            var memberId = DemoTenant.MemberId(memberNo);
            if (await db.Accounts.AnyAsync(a => a.MemberId == memberId && a.Kind == ProductKind.FixedDeposit, ct)) continue;
            try
            {
                await savings.OpenFixedDepositAsync(memberId, product, principal, LedgerSeeder.FosaAccount(memberNo), DemoTenant.Users.Teller, ct);
                created++;
            }
            catch (DomainException ex)
            {
                logger.LogWarning("Skipping fixed deposit for {Member}: {Reason}", memberNo, ex.Message);
            }
        }
        logger.Created("Fixed deposits", created);

        created = 0;
        if (!await db.Withdrawals.AnyAsync(ct))
        {
            // BOSA notice withdrawal awaiting approval (60-day notice running).
            await savings.RequestWithdrawalAsync(LedgerSeeder.SavingsAccount("M00004"), 5_000m, PayoutChannel.Cash, null, "School fees — notice given", DemoTenant.Users.Teller, ct);
            // FOSA withdrawal above the teller limit, awaiting branch manager approval.
            await savings.RequestWithdrawalAsync(LedgerSeeder.FosaAccount("M00015"), 12_000m, PayoutChannel.Cash, null, "Large cash withdrawal", DemoTenant.Users.Teller, ct);
            // FOSA withdrawal to M-Pesa awaiting approval (paid out by the payments module in Phase 5).
            await savings.RequestWithdrawalAsync(LedgerSeeder.FosaAccount("M00005"), 3_000m, PayoutChannel.MPesa, KenyanNames.Members.Single(m => m.MemberNumber == "M00005").Phone, "Send to M-Pesa", DemoTenant.Users.Teller, ct);
            created = 3;
        }
        logger.Created("Withdrawal requests", created);

        if (!await db.Dividends.AnyAsync(d => d.FinancialYear == 2025, ct))
        {
            await dividends.DeclareAsync(2025, shareRateBps: 800, depositRateBps: 600, DemoTenant.Users.Accountant, ct);
            logger.Created("Dividend declaration (pending approval)", 1);
        }
    }
}
