using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Domain;

public enum DividendStatus { Declared = 1, Approved = 2, Paid = 3, Rejected = 4 }

/// <summary>
/// Year-end dividend on shares and interest rebate on BOSA deposits. Declaring is the maker
/// step; approval (a different user) posts the appropriation to the GL; payment credits members'
/// FOSA accounts net of withholding tax. Computation is a skeleton: closing balances × rate.
/// </summary>
public class DividendDeclaration : TenantEntity
{
    private readonly List<DividendLine> _lines = [];
    private DividendDeclaration() { }

    public int FinancialYear { get; private set; }
    public int ShareDividendRateBps { get; private set; }
    public int DepositInterestRateBps { get; private set; }
    public int WithholdingTaxBps { get; private set; }
    public DividendStatus Status { get; private set; }
    public Guid DeclaredByUserId { get; private set; }
    public DateTimeOffset DeclaredAt { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public decimal TotalShareDividend { get; private set; }
    public decimal TotalDepositInterest { get; private set; }
    public decimal TotalWithholdingTax { get; private set; }
    public IReadOnlyList<DividendLine> Lines => _lines;

    public static DividendDeclaration Declare(Guid id, Guid tenantId, int year, int shareRateBps, int depositRateBps, int whtBps, Guid declaredBy, DateTimeOffset now, IEnumerable<DividendLineDraft> drafts)
    {
        if (shareRateBps < 0 || depositRateBps < 0 || whtBps < 0 || whtBps > 10_000) throw new DomainRuleException("savings.dividend.rate_invalid", "Rates must be non-negative basis points.");
        if (shareRateBps == 0 && depositRateBps == 0) throw new DomainRuleException("savings.dividend.rate_required", "Declare a share dividend rate, a deposit interest rate, or both.");
        var d = new DividendDeclaration
        {
            Id = id, TenantId = tenantId, FinancialYear = year, ShareDividendRateBps = shareRateBps, DepositInterestRateBps = depositRateBps, WithholdingTaxBps = whtBps,
            Status = DividendStatus.Declared, DeclaredByUserId = declaredBy, DeclaredAt = now,
        };
        foreach (var l in drafts)
        {
            var shareDiv = decimal.Round(l.ShareBalance * shareRateBps / 10_000m, 2, MidpointRounding.ToEven);
            var depInt = decimal.Round(l.DepositBalance * depositRateBps / 10_000m, 2, MidpointRounding.ToEven);
            if (shareDiv == 0 && depInt == 0) continue;
            var wht = decimal.Round((shareDiv + depInt) * whtBps / 10_000m, 2, MidpointRounding.ToEven);
            d._lines.Add(DividendLine.Create(d.Id, l, shareDiv, depInt, wht));
            d.TotalShareDividend += shareDiv; d.TotalDepositInterest += depInt; d.TotalWithholdingTax += wht;
        }
        if (d._lines.Count == 0) throw new DomainRuleException("savings.dividend.nothing_to_pay", "No member qualifies for a payout at these rates.");
        return d;
    }

    public void Approve(Guid approver, DateTimeOffset now)
    {
        if (Status != DividendStatus.Declared) throw new DomainRuleException("savings.dividend.not_declared", $"Declaration is {Status}.");
        MakerChecker.EnsureDistinct(DeclaredByUserId, approver, $"dividend declaration FY{FinancialYear}");
        Status = DividendStatus.Approved; ApprovedByUserId = approver; ApprovedAt = now;
    }

    public void Reject(Guid by, string reason)
    {
        if (Status != DividendStatus.Declared) throw new DomainRuleException("savings.dividend.not_declared", $"Declaration is {Status}.");
        MakerChecker.EnsureDistinct(DeclaredByUserId, by, $"dividend declaration FY{FinancialYear}");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("savings.dividend.reason_required", "A rejection reason is required.");
        Status = DividendStatus.Rejected; RejectionReason = reason.Trim();
    }

    public void MarkPaid(DateTimeOffset now)
    {
        if (Status != DividendStatus.Approved) throw new DomainRuleException("savings.dividend.not_approved", $"Declaration is {Status}.");
        Status = DividendStatus.Paid; PaidAt = now;
    }
}

public sealed record DividendLineDraft(Guid MemberId, string? SharesAccountNumber, decimal ShareBalance, string? DepositsAccountNumber, decimal DepositBalance, string? PayoutAccountNumber);

public class DividendLine
{
    private DividendLine() { }
    public Guid Id { get; private set; }
    public Guid DeclarationId { get; private set; }
    public Guid MemberId { get; private set; }
    public string? SharesAccountNumber { get; private set; }
    public decimal ShareBalance { get; private set; }
    public decimal ShareDividend { get; private set; }
    public string? DepositsAccountNumber { get; private set; }
    public decimal DepositBalance { get; private set; }
    public decimal DepositInterest { get; private set; }
    public decimal WithholdingTax { get; private set; }
    public decimal NetPayable => ShareDividend + DepositInterest - WithholdingTax;
    /// <summary>FOSA account credited on payment; null means the member has no FOSA account and the amount stays payable.</summary>
    public string? PayoutAccountNumber { get; private set; }
    public bool IsPaid { get; private set; }
    public string? JournalReference { get; private set; }

    internal static DividendLine Create(Guid declarationId, DividendLineDraft d, decimal shareDiv, decimal depInt, decimal wht) => new()
    {
        Id = Ids.New(), DeclarationId = declarationId, MemberId = d.MemberId, SharesAccountNumber = d.SharesAccountNumber, ShareBalance = d.ShareBalance, ShareDividend = shareDiv,
        DepositsAccountNumber = d.DepositsAccountNumber, DepositBalance = d.DepositBalance, DepositInterest = depInt, WithholdingTax = wht, PayoutAccountNumber = d.PayoutAccountNumber,
    };

    internal void MarkPaid(string journalReference) { IsPaid = true; JournalReference = journalReference; }
}
