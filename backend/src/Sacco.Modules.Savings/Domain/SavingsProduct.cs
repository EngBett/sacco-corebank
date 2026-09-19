using Sacco.Shared.Domain;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Domain;

/// <summary>A savings/share/deposit product. Every product is explicitly FOSA or BOSA (ADR 0002) and names its control GL account.</summary>
public class SavingsProduct : TenantEntity
{
    private SavingsProduct() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public ProductKind Kind { get; private set; }
    public Segment Segment { get; private set; }
    public string ControlGlAccountCode { get; private set; } = string.Empty;
    /// <summary>Suffix appended to the member number to form the account number, e.g. "FO" → M00001-FO.</summary>
    public string AccountSuffix { get; private set; } = string.Empty;
    public decimal MinimumOpeningDeposit { get; private set; }
    public decimal MinimumBalance { get; private set; }
    public bool AllowsWithdrawals { get; private set; }
    /// <summary>Days of notice before a withdrawal can be paid (BOSA deposits). 0 = on demand.</summary>
    public int WithdrawalNoticeDays { get; private set; }
    /// <summary>Cash withdrawals up to this amount are paid by the teller without a second approver. 0 = every withdrawal needs approval.</summary>
    public decimal TellerWithdrawalLimit { get; private set; }
    public decimal WithdrawalFee { get; private set; }
    public string? FeeIncomeGlAccountCode { get; private set; }
    /// <summary>Annual interest paid to the member, in basis points (e.g. 800 = 8% p.a.).</summary>
    public int InterestRateBps { get; private set; }
    public string? InterestExpenseGlAccountCode { get; private set; }
    /// <summary>Fixed deposit term. Null for non-FD products.</summary>
    public int? TermMonths { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>How the product appears on the public website (features, requirements, order, visibility).</summary>
    public PublicListing Listing { get; private set; } = new();

    public void SetListing(PublicListing listing) => Listing = listing;

    public static SavingsProduct Create(Guid id, Guid tenantId, string code, string name, string? description, ProductKind kind, Segment segment, string controlGl, string suffix,
        decimal minOpening, decimal minBalance, bool allowsWithdrawals, int noticeDays, decimal tellerLimit, decimal withdrawalFee, string? feeGl, int rateBps, string? interestGl, int? termMonths)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainRuleException("savings.product.code_required", "Product code is required.");
        if (string.IsNullOrWhiteSpace(suffix) || suffix.Length > 4) throw new DomainRuleException("savings.product.suffix_invalid", "Account suffix must be 1–4 characters.");
        var expectedSegment = kind == ProductKind.FosaCurrent ? Segment.Fosa : Segment.Bosa;
        if (segment != expectedSegment)
            throw new DomainRuleException("savings.product.segment_mismatch", $"{kind} products are {expectedSegment}; FOSA/BOSA is a fixed property of the product type.");
        if (kind == ProductKind.FixedDeposit && (termMonths is null or <= 0))
            throw new DomainRuleException("savings.product.term_required", "Fixed deposit products need a term in months.");
        if (kind == ProductKind.Shares && allowsWithdrawals)
            throw new DomainRuleException("savings.product.shares_not_withdrawable", "Share capital is not withdrawable except on exit.");
        if (minOpening < 0 || minBalance < 0 || withdrawalFee < 0 || tellerLimit < 0 || rateBps < 0 || noticeDays < 0)
            throw new DomainRuleException("savings.product.negative", "Product amounts cannot be negative.");
        if (withdrawalFee > 0 && string.IsNullOrWhiteSpace(feeGl))
            throw new DomainRuleException("savings.product.fee_gl_required", "A fee income GL account is required when a withdrawal fee is set.");
        if (rateBps > 0 && string.IsNullOrWhiteSpace(interestGl))
            throw new DomainRuleException("savings.product.interest_gl_required", "An interest expense GL account is required when an interest rate is set.");

        return new SavingsProduct
        {
            Id = id, TenantId = tenantId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), Description = description, Kind = kind, Segment = segment,
            ControlGlAccountCode = controlGl, AccountSuffix = suffix.ToUpperInvariant(), MinimumOpeningDeposit = minOpening, MinimumBalance = minBalance,
            AllowsWithdrawals = allowsWithdrawals, WithdrawalNoticeDays = noticeDays, TellerWithdrawalLimit = tellerLimit, WithdrawalFee = withdrawalFee,
            FeeIncomeGlAccountCode = feeGl, InterestRateBps = rateBps, InterestExpenseGlAccountCode = interestGl, TermMonths = termMonths, IsActive = true,
        };
    }

    public void Update(string name, string? description, decimal minOpening, decimal minBalance, int noticeDays, decimal tellerLimit, decimal withdrawalFee, string? feeGl, int rateBps, string? interestGl, bool isActive)
    {
        if (withdrawalFee > 0 && string.IsNullOrWhiteSpace(feeGl))
            throw new DomainRuleException("savings.product.fee_gl_required", "A fee income GL account is required when a withdrawal fee is set.");
        if (rateBps > 0 && string.IsNullOrWhiteSpace(interestGl))
            throw new DomainRuleException("savings.product.interest_gl_required", "An interest expense GL account is required when an interest rate is set.");
        Name = name.Trim(); Description = description; MinimumOpeningDeposit = minOpening; MinimumBalance = minBalance; WithdrawalNoticeDays = noticeDays;
        TellerWithdrawalLimit = tellerLimit; WithdrawalFee = withdrawalFee; FeeIncomeGlAccountCode = feeGl; InterestRateBps = rateBps; InterestExpenseGlAccountCode = interestGl; IsActive = isActive;
    }

    /// <summary>Simple interest for a fixed deposit held for its full term.</summary>
    public decimal FixedDepositInterest(decimal principal) =>
        TermMonths is int months ? decimal.Round(principal * InterestRateBps / 10_000m * months / 12m, 2, MidpointRounding.ToEven) : 0m;
}
