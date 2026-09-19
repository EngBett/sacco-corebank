using Sacco.Shared.Domain;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Domain;

public enum FeeTransactionType { Deposit = 1, Withdrawal = 2, BalanceEnquiry = 3 }
public enum FeeChannel { Cash = 1, MPesa = 2, AirtelMoney = 3, BankTransfer = 4, CheckOff = 5 }
public enum FeeChargeType { Fixed = 1, Percentage = 2, Tiered = 3 }
public enum FeeRuleStatus { PendingApproval = 1, Active = 2, Inactive = 3, Rejected = 4 }

/// <summary>The fee a transaction attracts and where it is credited. <see cref="RuleId"/> is null when it came from the product default (or no fee applies).</summary>
public sealed record FeeCharge(decimal Amount, string? GlAccountCode, Segment Segment, Guid? RuleId, string Basis)
{
    public static FeeCharge None(Segment segment) => new(0m, null, segment, null, "No fee");
    public static FeeCharge ProductDefault(SavingsProduct p) =>
        p.WithdrawalFee > 0 ? new(p.WithdrawalFee, p.FeeIncomeGlAccountCode, p.Segment, null, "Product default") : None(p.Segment);
}

/// <summary>One band of a tiered tariff: amounts above the previous band's <see cref="UpTo"/> and up to this one pay <see cref="Charge"/>. A null <see cref="UpTo"/> is "and above".</summary>
public class FeeTier
{
    private FeeTier() { }
    public FeeTier(decimal? upTo, decimal charge) { Id = Ids.New(); UpTo = upTo; Charge = charge; }
    public Guid Id { get; private set; }
    public Guid RuleId { get; private set; }
    public decimal? UpTo { get; private set; }
    public decimal Charge { get; private set; }
}

/// <summary>
/// One row of the fee matrix (ADR 0015). A rule prices a transaction type, optionally narrowed to a channel and a
/// product, over an amount range. Rules are maker-checker: created PendingApproval, live only once a different
/// user approves. A rule is never edited in place — a revision is a new rule that supersedes the old one on approval,
/// so the audit trail shows exactly what tariff applied when.
/// </summary>
public class FeeRule : TenantEntity
{
    private FeeRule() { }

    public FeeTransactionType TransactionType { get; private set; }
    /// <summary>Null = every channel.</summary>
    public FeeChannel? Channel { get; private set; }
    /// <summary>Null = every product.</summary>
    public string? ProductCode { get; private set; }
    public decimal MinAmount { get; private set; }
    /// <summary>Null = no upper bound.</summary>
    public decimal? MaxAmount { get; private set; }
    public FeeChargeType ChargeType { get; private set; }
    public decimal? FixedAmount { get; private set; }
    public int? RateBps { get; private set; }
    public decimal? MinCharge { get; private set; }
    public decimal? MaxCharge { get; private set; }
    public List<FeeTier> Tiers { get; private set; } = [];
    public string FeeIncomeGlAccountCode { get; private set; } = string.Empty;
    public Segment FeeIncomeSegment { get; private set; }
    public FeeRuleStatus Status { get; private set; }
    public Guid? SupersedesRuleId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public Guid? DeactivatedByUserId { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static FeeRule Create(Guid id, Guid tenantId, FeeTransactionType type, FeeChannel? channel, string? productCode, decimal minAmount, decimal? maxAmount,
        FeeChargeType chargeType, decimal? fixedAmount, int? rateBps, decimal? minCharge, decimal? maxCharge, IReadOnlyList<(decimal? UpTo, decimal Charge)> tiers,
        string feeIncomeGl, Segment feeIncomeSegment, Guid? supersedesRuleId, Guid createdBy, DateTimeOffset now)
    {
        static DomainRuleException Invalid(string code, string message) => new($"savings.fees.{code}", message);

        if (type == FeeTransactionType.BalanceEnquiry)
        {
            if (chargeType != FeeChargeType.Fixed) throw Invalid("enquiry_fixed_only", "A balance-enquiry fee is a fixed amount.");
            if (channel is not null) throw Invalid("enquiry_no_channel", "A balance-enquiry fee has no channel.");
            if (string.IsNullOrWhiteSpace(productCode)) throw Invalid("enquiry_product_required", "Choose the product whose balances the fee applies to.");
            minAmount = 0; maxAmount = null;
        }
        if (minAmount < 0 || (maxAmount is { } max && max <= minAmount)) throw Invalid("range_invalid", "The amount range must start at zero or more and end above its start.");

        var bands = tiers ?? [];
        switch (chargeType)
        {
            case FeeChargeType.Fixed:
                if (fixedAmount is not >= 0m) throw Invalid("fixed_amount_required", "A fixed fee needs an amount of zero or more.");
                if (rateBps is not null || bands.Count > 0 || minCharge is not null || maxCharge is not null)
                    throw Invalid("fixed_only_amount", "A fixed fee takes only an amount — no rate, tiers or caps.");
                break;
            case FeeChargeType.Percentage:
                if (rateBps is not (> 0 and <= 10_000)) throw Invalid("rate_invalid", "A percentage fee needs a rate between 0.01% and 100%.");
                if (fixedAmount is not null || bands.Count > 0) throw Invalid("percentage_only_rate", "A percentage fee takes a rate and optional caps — no fixed amount or tiers.");
                break;
            case FeeChargeType.Tiered:
                if (bands.Count == 0) throw Invalid("tiers_required", "A tiered fee needs at least one band.");
                if (fixedAmount is not null || rateBps is not null) throw Invalid("tiered_only_bands", "A tiered fee takes bands and optional caps — no fixed amount or rate.");
                for (var i = 0; i < bands.Count; i++)
                {
                    if (bands[i].Charge < 0) throw Invalid("tier_charge_negative", "Band charges cannot be negative.");
                    if (bands[i].UpTo is null && i != bands.Count - 1) throw Invalid("tier_open_not_last", "Only the last band can be open-ended.");
                    if (i > 0 && bands[i].UpTo is { } up && up <= bands[i - 1].UpTo) throw Invalid("tiers_not_ascending", "Band upper limits must strictly increase.");
                    if (bands[i].UpTo is <= 0m) throw Invalid("tier_limit_invalid", "Band upper limits must be positive.");
                }
                break;
        }
        if (minCharge < 0 || maxCharge < 0 || (minCharge is { } lo && maxCharge is { } hi && hi < lo)) throw Invalid("caps_invalid", "Caps cannot be negative and the maximum cannot be below the minimum.");
        if (string.IsNullOrWhiteSpace(feeIncomeGl)) throw Invalid("gl_required", "Name the income GL account the fee is credited to.");

        return new FeeRule
        {
            Id = id, TenantId = tenantId, TransactionType = type, Channel = channel, ProductCode = string.IsNullOrWhiteSpace(productCode) ? null : productCode.Trim().ToUpperInvariant(),
            MinAmount = minAmount, MaxAmount = maxAmount, ChargeType = chargeType, FixedAmount = fixedAmount, RateBps = rateBps, MinCharge = minCharge, MaxCharge = maxCharge,
            Tiers = bands.Select(b => new FeeTier(b.UpTo, b.Charge)).ToList(), FeeIncomeGlAccountCode = feeIncomeGl.Trim(), FeeIncomeSegment = feeIncomeSegment,
            Status = FeeRuleStatus.PendingApproval, SupersedesRuleId = supersedesRuleId, CreatedByUserId = createdBy, CreatedAt = now,
        };
    }

    public bool Applies(FeeTransactionType type, FeeChannel? channel, string productCode, decimal amount) =>
        Status == FeeRuleStatus.Active && TransactionType == type
        && (Channel is null || Channel == channel)
        && (ProductCode is null || string.Equals(ProductCode, productCode, StringComparison.OrdinalIgnoreCase))
        && (type == FeeTransactionType.BalanceEnquiry || (amount >= MinAmount && (MaxAmount is null || amount <= MaxAmount)));

    /// <summary>Product-specific beats any-product, channel-specific beats any-channel; then the narrower amount range wins.</summary>
    public (int Specificity, decimal Width) Precedence => ((ProductCode is null ? 0 : 2) + (Channel is null ? 0 : 1), (MaxAmount ?? decimal.MaxValue) - MinAmount);

    public bool Overlaps(FeeRule other) =>
        other.TransactionType == TransactionType && other.Channel == Channel && string.Equals(other.ProductCode, ProductCode, StringComparison.OrdinalIgnoreCase)
        && MinAmount <= (other.MaxAmount ?? decimal.MaxValue) && other.MinAmount <= (MaxAmount ?? decimal.MaxValue);

    public decimal Calculate(decimal amount)
    {
        var raw = ChargeType switch
        {
            FeeChargeType.Fixed => FixedAmount!.Value,
            FeeChargeType.Percentage => amount * RateBps!.Value / 10_000m,
            _ => (Tiers.OrderBy(t => t.UpTo ?? decimal.MaxValue).FirstOrDefault(t => t.UpTo is null || amount <= t.UpTo)
                  ?? throw new DomainRuleException("savings.fees.no_tier", $"No fee band covers {amount:N2}.")).Charge,
        };
        if (MinCharge is { } min && raw < min) raw = min;
        if (MaxCharge is { } max && raw > max) raw = max;
        return decimal.Round(raw, 2, MidpointRounding.AwayFromZero);
    }

    public string Describe() => ChargeType switch
    {
        FeeChargeType.Fixed => $"KES {FixedAmount:N2}",
        FeeChargeType.Percentage => $"{RateBps / 100m:0.##}%{Caps()}",
        _ => $"{Tiers.Count} bands{Caps()}",
    };

    private string Caps() => (MinCharge, MaxCharge) switch
    {
        (null, null) => "",
        ({ } lo, null) => $" (min {lo:N0})",
        (null, { } hi) => $" (max {hi:N0})",
        ({ } lo, { } hi) => $" ({lo:N0}–{hi:N0})",
    };

    public FeeCharge ToCharge(decimal amount) => new(Calculate(amount), FeeIncomeGlAccountCode, FeeIncomeSegment, Id, Describe());

    public void Approve(Guid approver, DateTimeOffset now)
    {
        if (Status != FeeRuleStatus.PendingApproval) throw new DomainRuleException("savings.fees.not_pending", $"Fee rule is {Status}.");
        MakerChecker.EnsureDistinct(CreatedByUserId, approver, $"fee rule {Id}");
        Status = FeeRuleStatus.Active; DecidedByUserId = approver; DecidedAt = now;
    }

    public void Reject(Guid by, string reason, DateTimeOffset now)
    {
        if (Status != FeeRuleStatus.PendingApproval) throw new DomainRuleException("savings.fees.not_pending", $"Fee rule is {Status}.");
        MakerChecker.EnsureDistinct(CreatedByUserId, by, $"fee rule {Id}");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("savings.fees.reason_required", "A rejection reason is required.");
        Status = FeeRuleStatus.Rejected; DecidedByUserId = by; DecidedAt = now; RejectionReason = reason.Trim();
    }

    public void Deactivate(Guid by, DateTimeOffset now)
    {
        if (Status != FeeRuleStatus.Active) throw new DomainRuleException("savings.fees.not_active", $"Fee rule is {Status}.");
        Status = FeeRuleStatus.Inactive; DeactivatedByUserId = by; DeactivatedAt = now;
    }
}
