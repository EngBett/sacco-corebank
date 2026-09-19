using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Ledger.Domain;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Domain;

namespace Sacco.Seed.Seeders;

/// <summary>
/// Icodeio SACCO chart of accounts. Every account is explicitly FOSA or BOSA (ADR 0002). Other
/// seeders and modules reference accounts by the constants below, never by literal codes.
/// </summary>
public static class Coa
{
    // ---- BOSA assets ----
    public const string BosaCashAtBank = "1000";
    public const string BosaLoansControl = "1100";
    public const string BosaInterestReceivable = "1200";
    public const string BosaInvestments = "1300";
    public const string BosaDueFromFosa = "1800";           // inter-segment clearing (asset side)
    public const string BosaLoanLossProvision = "1900";     // contra-asset
    // ---- FOSA assets ----
    public const string FosaCashOnHand = "1010";
    public const string FosaCashAtBank = "1020";
    public const string FosaMpesaSettlement = "1030";
    public const string FosaAirtelSettlement = "1040";
    public const string FosaLoansControl = "1150";
    public const string FosaInterestReceivable = "1250";
    public const string FosaLoanLossProvision = "1950";     // contra-asset
    // ---- BOSA liabilities ----
    public const string BosaMemberDepositsControl = "2000";
    public const string BosaFixedDepositsControl = "2100";
    public const string BosaInterestPayable = "2300";
    public const string BosaDividendsPayable = "2400";
    public const string BosaSdgfPayable = "2600";
    public const string BosaWithholdingTaxPayable = "2700";
    // ---- FOSA liabilities ----
    public const string FosaSavingsControl = "2200";
    public const string FosaSuspense = "2500";
    public const string FosaDueToBosa = "2800";             // inter-segment clearing (liability side)
    // ---- Equity (BOSA) ----
    public const string ShareCapitalControl = "3000";
    public const string StatutoryReserve = "3100";
    public const string RetainedEarnings = "3200";
    public const string InstitutionalCapital = "3300";
    // ---- Income ----
    public const string BosaLoanInterestIncome = "4000";
    public const string FosaLoanInterestIncome = "4010";
    public const string BosaLoanFees = "4100";
    public const string FosaFeesAndCharges = "4200";
    public const string BosaInvestmentIncome = "4300";
    public const string BosaEntranceFees = "4400";
    // ---- Expenses ----
    public const string BosaInterestExpense = "5000";
    public const string BosaProvisionExpense = "5100";
    public const string FosaProvisionExpense = "5110";
    public const string BosaStaffCosts = "5200";
    public const string FosaStaffCosts = "5210";
    public const string BosaSdgfExpense = "5400";
    public const string FosaBankCharges = "5500";
    public const string FosaMobileMoneyCharges = "5510";
}

public sealed class ChartOfAccountsSeeder(LedgerDbContext db, ILogger<ChartOfAccountsSeeder> logger) : ISeeder
{
    public int Order => 10;

    private sealed record Def(string Code, string Name, GlAccountCategory Category, Segment Segment, bool Postable = true, bool Control = false, string? Parent = null, EntryDirection? Normal = null, string? Description = null);

    private static readonly Def[] Accounts =
    [
        // Headers
        new("1", "Assets", GlAccountCategory.Asset, Segment.Bosa, Postable: false),
        new("2", "Liabilities", GlAccountCategory.Liability, Segment.Bosa, Postable: false),
        new("3", "Equity", GlAccountCategory.Equity, Segment.Bosa, Postable: false),
        new("4", "Income", GlAccountCategory.Income, Segment.Bosa, Postable: false),
        new("5", "Expenses", GlAccountCategory.Expense, Segment.Bosa, Postable: false),

        // BOSA assets
        new(Coa.BosaCashAtBank, "BOSA Cash at Bank", GlAccountCategory.Asset, Segment.Bosa, Parent: "1"),
        new(Coa.BosaLoansControl, "BOSA Loans to Members (control)", GlAccountCategory.Asset, Segment.Bosa, Control: true, Parent: "1"),
        new(Coa.BosaInterestReceivable, "BOSA Interest Receivable", GlAccountCategory.Asset, Segment.Bosa, Parent: "1"),
        new(Coa.BosaInvestments, "BOSA Investments", GlAccountCategory.Asset, Segment.Bosa, Parent: "1"),
        new(Coa.BosaDueFromFosa, "Due from FOSA (inter-segment clearing)", GlAccountCategory.Asset, Segment.Bosa, Parent: "1", Description: "Mirror of 2800. Nets to zero on consolidation."),
        new(Coa.BosaLoanLossProvision, "BOSA Loan Loss Provision", GlAccountCategory.Asset, Segment.Bosa, Parent: "1", Normal: EntryDirection.Credit, Description: "Contra-asset; SASRA provisioning per aging bucket."),
        // FOSA assets
        new(Coa.FosaCashOnHand, "FOSA Cash on Hand (teller)", GlAccountCategory.Asset, Segment.Fosa, Parent: "1"),
        new(Coa.FosaCashAtBank, "FOSA Cash at Bank", GlAccountCategory.Asset, Segment.Fosa, Parent: "1"),
        new(Coa.FosaMpesaSettlement, "M-Pesa Settlement Account", GlAccountCategory.Asset, Segment.Fosa, Parent: "1"),
        new(Coa.FosaAirtelSettlement, "Airtel Money Settlement Account", GlAccountCategory.Asset, Segment.Fosa, Parent: "1"),
        new(Coa.FosaLoansControl, "FOSA Loans to Members (control)", GlAccountCategory.Asset, Segment.Fosa, Control: true, Parent: "1"),
        new(Coa.FosaInterestReceivable, "FOSA Interest Receivable", GlAccountCategory.Asset, Segment.Fosa, Parent: "1"),
        new(Coa.FosaLoanLossProvision, "FOSA Loan Loss Provision", GlAccountCategory.Asset, Segment.Fosa, Parent: "1", Normal: EntryDirection.Credit),
        // BOSA liabilities
        new(Coa.BosaMemberDepositsControl, "BOSA Member Deposits (control)", GlAccountCategory.Liability, Segment.Bosa, Control: true, Parent: "2", Description: "Non-withdrawable / notice-period deposits that back loan eligibility."),
        new(Coa.BosaFixedDepositsControl, "BOSA Fixed Deposits (control)", GlAccountCategory.Liability, Segment.Bosa, Control: true, Parent: "2"),
        new(Coa.BosaInterestPayable, "Interest Payable on Deposits", GlAccountCategory.Liability, Segment.Bosa, Parent: "2"),
        new(Coa.BosaDividendsPayable, "Dividends Payable", GlAccountCategory.Liability, Segment.Bosa, Parent: "2"),
        new(Coa.BosaSdgfPayable, "SDGF Contribution Payable", GlAccountCategory.Liability, Segment.Bosa, Parent: "2"),
        new(Coa.BosaWithholdingTaxPayable, "Withholding Tax Payable (KRA)", GlAccountCategory.Liability, Segment.Bosa, Parent: "2", Description: "5% WHT withheld on dividends and deposit interest."),
        // FOSA liabilities
        new(Coa.FosaSavingsControl, "FOSA Savings & Current Accounts (control)", GlAccountCategory.Liability, Segment.Fosa, Control: true, Parent: "2"),
        new(Coa.FosaSuspense, "FOSA Suspense / Unallocated Receipts", GlAccountCategory.Liability, Segment.Fosa, Parent: "2"),
        new(Coa.FosaDueToBosa, "Due to BOSA (inter-segment clearing)", GlAccountCategory.Liability, Segment.Fosa, Parent: "2", Description: "Mirror of 1800. Nets to zero on consolidation."),
        // Equity
        new(Coa.ShareCapitalControl, "Member Share Capital (control)", GlAccountCategory.Equity, Segment.Bosa, Control: true, Parent: "3"),
        new(Coa.StatutoryReserve, "Statutory Reserve", GlAccountCategory.Equity, Segment.Bosa, Parent: "3"),
        new(Coa.RetainedEarnings, "Retained Earnings", GlAccountCategory.Equity, Segment.Bosa, Parent: "3"),
        new(Coa.InstitutionalCapital, "Institutional Capital", GlAccountCategory.Equity, Segment.Bosa, Parent: "3"),
        // Income
        new(Coa.BosaLoanInterestIncome, "BOSA Interest Income on Loans", GlAccountCategory.Income, Segment.Bosa, Parent: "4"),
        new(Coa.FosaLoanInterestIncome, "FOSA Interest Income on Loans", GlAccountCategory.Income, Segment.Fosa, Parent: "4"),
        new(Coa.BosaLoanFees, "BOSA Loan Processing Fees", GlAccountCategory.Income, Segment.Bosa, Parent: "4"),
        new(Coa.FosaFeesAndCharges, "FOSA Transaction Fees & Charges", GlAccountCategory.Income, Segment.Fosa, Parent: "4"),
        new(Coa.BosaInvestmentIncome, "BOSA Investment Income", GlAccountCategory.Income, Segment.Bosa, Parent: "4"),
        new(Coa.BosaEntranceFees, "Membership Entrance Fees", GlAccountCategory.Income, Segment.Bosa, Parent: "4"),
        // Expenses
        new(Coa.BosaInterestExpense, "BOSA Interest Expense on Deposits", GlAccountCategory.Expense, Segment.Bosa, Parent: "5"),
        new(Coa.BosaProvisionExpense, "BOSA Loan Loss Provision Expense", GlAccountCategory.Expense, Segment.Bosa, Parent: "5"),
        new(Coa.FosaProvisionExpense, "FOSA Loan Loss Provision Expense", GlAccountCategory.Expense, Segment.Fosa, Parent: "5"),
        new(Coa.BosaStaffCosts, "BOSA Staff Costs", GlAccountCategory.Expense, Segment.Bosa, Parent: "5"),
        new(Coa.FosaStaffCosts, "FOSA Staff Costs", GlAccountCategory.Expense, Segment.Fosa, Parent: "5"),
        new(Coa.BosaSdgfExpense, "SDGF Contribution Expense", GlAccountCategory.Expense, Segment.Bosa, Parent: "5"),
        new(Coa.FosaBankCharges, "FOSA Bank Charges", GlAccountCategory.Expense, Segment.Fosa, Parent: "5"),
        new(Coa.FosaMobileMoneyCharges, "FOSA Mobile Money Charges", GlAccountCategory.Expense, Segment.Fosa, Parent: "5"),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.GlAccounts.ToDictionaryAsync(a => a.Code, ct);
        var created = 0;
        foreach (var d in Accounts)
        {
            if (existing.ContainsKey(d.Code)) continue;
            Guid? parentId = d.Parent is null ? null : existing[d.Parent].Id;
            var acct = GlAccount.Create(Ids.Deterministic($"gl:{DemoTenant.Slug}:{d.Code}"), DemoTenant.Id, d.Code, d.Name, d.Category, d.Segment, d.Postable, d.Control, parentId, d.Description, d.Normal);
            db.GlAccounts.Add(acct);
            existing[d.Code] = acct;
            created++;
        }
        await db.SaveChangesAsync(ct);
        logger.Created("GL accounts", created);
    }
}
