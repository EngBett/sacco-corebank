using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Lending.Domain;

public enum InterestMethod { ReducingBalance = 1, Flat = 2 }

/// <summary>How the SACCO groups the loan for members (and on the public site): salary-based FOSA loans, deposit-based
/// BOSA loans, or micro, small and medium enterprise (MSME) loans for groups, traders and farmers.</summary>
public enum LoanCategory { Fosa = 1, Bosa = 2, Msme = 3 }

/// <summary>Loan product. Segment, GL mapping, eligibility, guarantee and approval policy are all configuration.</summary>
public class LoanProduct : TenantEntity
{
    private LoanProduct() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Segment Segment { get; private set; }
    public string ControlGlAccountCode { get; private set; } = string.Empty;
    public string InterestIncomeGlAccountCode { get; private set; } = string.Empty;
    public string InterestReceivableGlAccountCode { get; private set; } = string.Empty;
    public string FeeIncomeGlAccountCode { get; private set; } = string.Empty;
    public string ProvisionGlAccountCode { get; private set; } = string.Empty;
    public string ProvisionExpenseGlAccountCode { get; private set; } = string.Empty;
    public int InterestRateBps { get; private set; }
    public InterestMethod InterestMethod { get; private set; }
    public decimal MinAmount { get; private set; }
    public decimal MaxAmount { get; private set; }
    public int MinTermMonths { get; private set; }
    public int MaxTermMonths { get; private set; }
    /// <summary>Maximum loan = BOSA deposits × this multiplier (e.g. 3.0). 0 = no deposit-based cap.</summary>
    public decimal DepositMultiplier { get; private set; }
    public int MinMembershipMonths { get; private set; }
    public int ProcessingFeeBps { get; private set; }
    public bool RequiresGuarantors { get; private set; }
    public int MinGuarantors { get; private set; }
    /// <summary>Loans above this amount need <see cref="CommitteeApprovalsRequired"/> distinct approvers; at or below need one.</summary>
    public decimal CommitteeThreshold { get; private set; }
    public int CommitteeApprovalsRequired { get; private set; }
    public int GracePeriodDays { get; private set; }
    /// <summary>Defaults to the segment (FOSA/BOSA); MSME loans are set explicitly.</summary>
    public LoanCategory Category { get; private set; }

    /// <summary>How the product appears on the public website (features, requirements, order, visibility).</summary>
    public PublicListing Listing { get; private set; } = new();

    public void SetListing(LoanCategory category, PublicListing listing)
    {
        if (!Enum.IsDefined(category)) throw new DomainRuleException("loans.product.category_invalid", "Choose FOSA, BOSA or MSME.");
        Category = category;
        Listing = listing;
    }

    public bool IsActive { get; private set; }

    public static LoanProduct Create(Guid id, Guid tenantId, string code, string name, string? description, Segment segment,
        string controlGl, string interestIncomeGl, string interestReceivableGl, string feeIncomeGl, string provisionGl, string provisionExpenseGl,
        int rateBps, InterestMethod method, decimal minAmount, decimal maxAmount, int minTerm, int maxTerm, decimal depositMultiplier, int minMembershipMonths,
        int processingFeeBps, bool requiresGuarantors, int minGuarantors, decimal committeeThreshold, int committeeApprovals, int graceDays)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainRuleException("loans.product.code_required", "Product code is required.");
        if (rateBps < 0 || minAmount < 0 || maxAmount < minAmount || minTerm <= 0 || maxTerm < minTerm || depositMultiplier < 0 || processingFeeBps < 0 || graceDays < 0)
            throw new DomainRuleException("loans.product.invalid", "Product limits are inconsistent.");
        if (committeeApprovals < 1) throw new DomainRuleException("loans.product.approvals", "At least one approval is required.");
        if (requiresGuarantors && minGuarantors < 1) throw new DomainRuleException("loans.product.guarantors", "A product that requires guarantors needs MinGuarantors ≥ 1.");
        return new LoanProduct
        {
            Id = id, TenantId = tenantId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), Description = description, Segment = segment,
            Category = segment == Segment.Fosa ? LoanCategory.Fosa : LoanCategory.Bosa,
            ControlGlAccountCode = controlGl, InterestIncomeGlAccountCode = interestIncomeGl, InterestReceivableGlAccountCode = interestReceivableGl, FeeIncomeGlAccountCode = feeIncomeGl,
            ProvisionGlAccountCode = provisionGl, ProvisionExpenseGlAccountCode = provisionExpenseGl,
            InterestRateBps = rateBps, InterestMethod = method, MinAmount = minAmount, MaxAmount = maxAmount, MinTermMonths = minTerm, MaxTermMonths = maxTerm,
            DepositMultiplier = depositMultiplier, MinMembershipMonths = minMembershipMonths, ProcessingFeeBps = processingFeeBps, RequiresGuarantors = requiresGuarantors, MinGuarantors = minGuarantors,
            CommitteeThreshold = committeeThreshold, CommitteeApprovalsRequired = committeeApprovals, GracePeriodDays = graceDays, IsActive = true,
        };
    }

    public void Update(string name, string? description, int rateBps, decimal minAmount, decimal maxAmount, int minTerm, int maxTerm, decimal depositMultiplier, int minMembershipMonths,
        int processingFeeBps, bool requiresGuarantors, int minGuarantors, decimal committeeThreshold, int committeeApprovals, int graceDays, bool isActive)
    {
        if (rateBps < 0 || minAmount < 0 || maxAmount < minAmount || minTerm <= 0 || maxTerm < minTerm || committeeApprovals < 1)
            throw new DomainRuleException("loans.product.invalid", "Product limits are inconsistent.");
        Name = name.Trim(); Description = description; InterestRateBps = rateBps; MinAmount = minAmount; MaxAmount = maxAmount; MinTermMonths = minTerm; MaxTermMonths = maxTerm;
        DepositMultiplier = depositMultiplier; MinMembershipMonths = minMembershipMonths; ProcessingFeeBps = processingFeeBps; RequiresGuarantors = requiresGuarantors; MinGuarantors = minGuarantors;
        CommitteeThreshold = committeeThreshold; CommitteeApprovalsRequired = committeeApprovals; GracePeriodDays = graceDays; IsActive = isActive;
    }

    public int ApprovalsRequiredFor(decimal amount) => amount > CommitteeThreshold ? CommitteeApprovalsRequired : 1;
}
