using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Lending.Application;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Domain;
using Sacco.Shared.Lending;
using Sacco.Shared.Time;

namespace Sacco.Seed.Seeders;

/// <summary>
/// Loan products, the SASRA provisioning schedule, and loans in every state: one per aging bucket
/// (arranged by back-dating disbursement), a healthy current loan, one awaiting appraisal, one
/// awaiting its second committee approval, and a guarantor near her exposure cap. Ends with a
/// provisioning run awaiting approval.
/// </summary>
public sealed class LendingSeeder(LendingDbContext db, LoanService loans, RepaymentService repayments, ProvisioningService provisioning, CreditScoringService scoring, Sacco.Shared.Savings.ISavingsService savings, IClock clock, ILogger<LendingSeeder> logger) : ISeeder
{
    public int Order => 30;

    public const string Development = "DEV-LOAN";
    public const string Emergency = "EMG-LOAN";
    public const string SalaryAdvance = "SAL-ADV";

    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedProductsAsync(ct);
        await SeedProvisioningConfigAsync(ct);
        await SeedScorecardAsync(ct);
        if (await db.Loans.AnyAsync(ct)) { logger.LogInformation("  Loans already seeded"); return; }

        var today = clock.Today;
        var officer = DemoTenant.Users.LoanOfficer;

        // Larger BOSA balances for the members the demos borrow against (check-off lump sums; idempotent by reference).
        foreach (var (memberNo, amount) in new[] { ("M00009", 60_000m), ("M00015", 50_000m), ("M00006", 40_000m), ("M00003", 40_000m), ("M00010", 30_000m) })
        {
            try { await savings.DepositAsync(new Sacco.Shared.Savings.DepositCommand(LedgerSeeder.SavingsAccount(memberNo), amount, Sacco.Shared.Savings.DepositChannel.CheckOff, $"SEED-LUMP:{memberNo}", "Employer lump-sum remittance", DemoTenant.Users.Teller), ct); }
            catch (ConflictException) { }
        }
        var c1 = DemoTenant.Users.CreditCommittee1;
        var c2 = DemoTenant.Users.CreditCommittee2;
        var manager = DemoTenant.Users.BranchManager;

        // ---- Loans in every aging bucket (disbursed in the past, instalments unpaid for N days) ----
        // Normal (current, paid up), Watch (~60d), Substandard (~200d), Doubtful (~400d), Loss (~800d).
        var buckets = new (string borrower, string product, decimal amount, int term, int monthsAgo, int instalmentsPaid, string purpose)[]
        {
            ("M00001", Development, 90_000m, 24, 8, 8, "School fees — university tuition"),           // Normal: all due instalments paid
            ("M00002", Development, 60_000m, 12, 6, 3, "Dairy cow purchase"),                         // Watch: ~56 days past grace
            ("M00004", Emergency, 30_000m, 12, 10, 2, "Hospital bill"),                                // Substandard: ~210 days
            ("M00006", Development, 120_000m, 36, 17, 3, "Plot purchase deposit"),                    // Doubtful: ~390 days
            ("M00010", Development, 80_000m, 24, 30, 3, "Retirement business — hardware shop"),       // Loss: ~27 months overdue
            ("M00013", SalaryAdvance, 15_000m, 3, 1, 1, "Salary advance"),                            // Normal, FOSA product, no guarantor
        };

        var created = 0;
        foreach (var (borrower, product, amount, term, monthsAgo, paid, purpose) in buckets)
        {
            var disbursed = today.AddMonths(-monthsAgo);
            var loan = await ApplyGuaranteeApproveDisburse(borrower, product, amount, term, purpose, disbursed, ct);
            for (var i = 1; i <= Math.Min(paid, term); i++)
            {
                var inst = loan.Schedule.OrderBy(s => s.Number).ElementAt(i - 1);
                // Paid on the due date, so the repayment-history factor sees an on-time record.
                await repayments.RepayAsync(new RepaymentCommand(loan.LoanNumber, inst.PrincipalDue + inst.InterestDue, RepaymentChannel.CheckOff, $"SEED-REPAY:{loan.LoanNumber}:{i}", "Check-off remittance", DemoTenant.Users.Teller, inst.DueDate <= today ? inst.DueDate : today), ct);
            }
            created++;
        }
        logger.Created("Loans disbursed", created);

        // Interest due to date is recognised in the GL.
        var accrued = await repayments.AccrueInterestAsync(today, DemoTenant.Users.System, ct);
        logger.Created("Interest accruals", accrued);

        // ---- Awaiting appraisal ----
        var applied = await loans.ApplyAsync(DemoTenant.MemberId("M00007"), Emergency, 25_000m, 12, "Medical emergency — surgery", null, officer, ct);
        await GuaranteeUpTo(applied.Id, "M00007", 25_000m, ct);

        // ---- Awaiting second committee approval (above the 100,000 committee threshold → 2 approvals) ----
        var large = await loans.ApplyAsync(DemoTenant.MemberId("M00009"), Development, 150_000m, 36, "Rental houses construction", null, officer, ct);
        await GuaranteeUpTo(large.Id, "M00009", 150_000m, ct);
        await loans.AppraiseAsync(large.Id, "Income verified from KPLC payslips; deposits 4× monthly instalment.", officer, ct);
        await loans.ApproveAsync(large.Id, "Approved subject to second signature.", c1, ct);
        logger.Created("Loans pending (1 awaiting appraisal, 1 awaiting 2nd approval)", 2);

        // ---- Provisioning run awaiting approval ----
        await provisioning.ComputeRunAsync(today, DemoTenant.Users.Accountant, ct);
        logger.Created("Provisioning run (pending approval)", 1);
    }

    /// <summary>Guarantor pool: every member with accounts, M00003 first so she ends up close to the 100%-of-deposits cap.</summary>
    private static readonly string[] HeavyGuarantors =
        ["M00003", .. KenyanNames.Members.Where(m => m.HasAccounts && m.Kyc == "Verified" && m.MemberNumber != "M00003").Select(m => m.MemberNumber)];

    private async Task<Loan> ApplyGuaranteeApproveDisburse(string borrower, string productCode, decimal amount, int term, string purpose, DateOnly disbursedOn, CancellationToken ct)
    {
        var product = await loans.GetProductAsync(productCode, ct);
        var eligibility = await loans.EvaluateEligibilityAsync(DemoTenant.MemberId(borrower), product, ct);
        amount = Math.Max(product.MinAmount, Math.Min(amount, Math.Floor(eligibility.MaxEligibleAmount / 1_000m) * 1_000m));
        var loan = await loans.ApplyAsync(DemoTenant.MemberId(borrower), productCode, amount, term, purpose, null, DemoTenant.Users.LoanOfficer, ct);
        if (product.RequiresGuarantors) await GuaranteeUpTo(loan.Id, borrower, amount, ct);
        await loans.AppraiseAsync(loan.Id, $"Appraised: {purpose}. Deposits and guarantees adequate.", DemoTenant.Users.LoanOfficer, ct);
        await loans.ApproveAsync(loan.Id, null, DemoTenant.Users.CreditCommittee1, ct);
        var current = await loans.GetAsync(loan.Id, ct);
        if (current.Status != LoanStatus.Approved) await loans.ApproveAsync(loan.Id, null, DemoTenant.Users.CreditCommittee2, ct);
        return await loans.DisburseAsync(loan.Id, DemoTenant.Users.BranchManager, disbursedOn, ct);
    }

    /// <summary>Spreads guarantees across the pool by each guarantor's remaining room (capacity and free deposits).</summary>
    private async Task GuaranteeUpTo(Guid loanId, string borrower, decimal amount, CancellationToken ct)
    {
        var loan = await loans.GetAsync(loanId, ct);
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == loan.ProductId, ct);
        // Spread across at least MinGuarantors people (and never more than half the loan on one guarantor when two are needed).
        var maxSlice = Math.Ceiling(amount / Math.Max(product.MinGuarantors, 1) / 1_000m) * 1_000m;
        var remaining = amount;
        foreach (var g in HeavyGuarantors.Where(g => g != borrower))
        {
            if (remaining <= 0) break;
            var exposure = await loans.GetGuarantorExposureAsync(DemoTenant.MemberId(g), ct);
            var room = Math.Min(exposure.Capacity - exposure.ActiveGuarantees, exposure.AvailableDeposits);
            var slice = Math.Min(Math.Min(remaining, maxSlice), Math.Floor(room / 1_000m) * 1_000m);
            if (slice < 2_000m) continue;
            await AddAndAcceptGuarantor(loanId, g, slice, ct);
            remaining -= slice;
        }
    }

    private async Task AddAndAcceptGuarantor(Guid loanId, string memberNo, decimal amount, CancellationToken ct)
    {
        var loan = await loans.AddGuarantorAsync(loanId, DemoTenant.MemberId(memberNo), amount, DemoTenant.Users.LoanOfficer, ct);
        var g = loan.Guarantors.Single(x => x.GuarantorMemberId == DemoTenant.MemberId(memberNo));
        await loans.AcceptGuaranteeAsync(loanId, g.Id, DemoTenant.Users.LoanOfficer, ct);
    }

    private async Task SeedProductsAsync(CancellationToken ct)
    {
        var existing = await db.Products.Select(p => p.Code).ToHashSetAsync(ct);
        var defs = new[]
        {
            (Development, "Development Loan", "Long-term BOSA loan up to 3× deposits; 12% p.a. reducing balance; 2 guarantors; committee approval above KES 100,000.",
                Segment.Bosa, Coa.BosaLoansControl, Coa.BosaLoanInterestIncome, Coa.BosaInterestReceivable, Coa.BosaLoanFees, Coa.BosaLoanLossProvision, Coa.BosaProvisionExpense,
                1200, InterestMethod.ReducingBalance, 10_000m, 3_000_000m, 6, 48, 3.0m, 6, 100, true, 2, 100_000m, 2, 5),
            (Emergency, "Emergency Loan", "Short-term BOSA loan up to 2× deposits; 14% p.a. reducing; 1 guarantor.",
                Segment.Bosa, Coa.BosaLoansControl, Coa.BosaLoanInterestIncome, Coa.BosaInterestReceivable, Coa.BosaLoanFees, Coa.BosaLoanLossProvision, Coa.BosaProvisionExpense,
                1400, InterestMethod.ReducingBalance, 5_000m, 200_000m, 3, 12, 2.0m, 3, 100, true, 1, 100_000m, 2, 5),
            (SalaryAdvance, "FOSA Salary Advance", "FOSA advance against salary; 5% flat over 1–3 months; no guarantor.",
                Segment.Fosa, Coa.FosaLoansControl, Coa.FosaLoanInterestIncome, Coa.FosaInterestReceivable, Coa.FosaFeesAndCharges, Coa.FosaLoanLossProvision, Coa.FosaProvisionExpense,
                500, InterestMethod.Flat, 1_000m, 100_000m, 1, 3, 0m, 1, 0, false, 0, 100_000m, 1, 0),
        };
        var created = 0;
        foreach (var d in defs)
        {
            if (existing.Contains(d.Item1)) continue;
            db.Products.Add(LoanProduct.Create(Ids.Deterministic($"loanproduct:{DemoTenant.Slug}:{d.Item1}"), DemoTenant.Id, d.Item1, d.Item2, d.Item3, d.Item4, d.Item5, d.Item6, d.Item7, d.Item8, d.Item9, d.Item10,
                d.Item11, d.Item12, d.Item13, d.Item14, d.Item15, d.Item16, d.Item17, d.Item18, d.Item19, d.Item20, d.Item21, d.Item22, d.Item23, d.Item24));
            created++;
        }
        await db.SaveChangesAsync(ct);
        logger.Created("Loan products", created);
    }

    private async Task SeedScorecardAsync(CancellationToken ct)
    {
        if (await db.Scorecards.AnyAsync(ct)) return;
        await scoring.SetScorecardAsync(Scorecard.DefaultFactors, 70, 50, true, "Demo SACCO credit policy v1 — platform default weights; review with the credit committee", DemoTenant.Users.System, ct);
        logger.Created("Credit scorecard", 1);
    }

    private async Task SeedProvisioningConfigAsync(CancellationToken ct)
    {
        if (await db.ProvisioningConfigs.AnyAsync(ct)) return;
        // Sacco Societies (Deposit-Taking Sacco Business) Regulations schedule. Confirm current circular before Phase 6.
        await provisioning.SetConfigAsync(
        [
            new AgingBucketDraft("Performing", 0, 100),
            new AgingBucketDraft("Watch", 31, 500),
            new AgingBucketDraft("Substandard", 181, 2500),
            new AgingBucketDraft("Doubtful", 361, 5000),
            new AgingBucketDraft("Loss", 721, 10000),
        ], "Sacco Societies (DT-Sacco Business) Regulations, provisioning schedule — verify against current SASRA circular", DemoTenant.Users.System, ct);
        logger.Created("Provisioning config", 1);
    }
}
