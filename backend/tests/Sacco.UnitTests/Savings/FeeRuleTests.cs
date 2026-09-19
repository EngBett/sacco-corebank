using Sacco.Modules.Savings.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Savings;

public class FeeRuleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Maker = Guid.NewGuid();
    private static readonly Guid Checker = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private static FeeRule Rule(FeeTransactionType type = FeeTransactionType.Withdrawal, FeeChannel? channel = FeeChannel.MPesa, string? product = "FOSA-CUR",
        decimal min = 1m, decimal? max = 150_000m, FeeChargeType charge = FeeChargeType.Fixed, decimal? fixedAmount = 45m, int? rateBps = null,
        decimal? minCharge = null, decimal? maxCharge = null, (decimal? UpTo, decimal Charge)[]? tiers = null, bool approve = true)
    {
        var rule = FeeRule.Create(Guid.NewGuid(), Tenant, type, channel, product, min, max, charge, fixedAmount, rateBps, minCharge, maxCharge, tiers ?? [], "4200", Segment.Fosa, null, Maker, Now);
        if (approve) rule.Approve(Checker, Now);
        return rule;
    }

    [Fact]
    public void Fixed_percentage_and_tiered_charges_calculate_with_caps()
    {
        Rule().Calculate(10_000m).ShouldBe(45m);

        var percentage = Rule(charge: FeeChargeType.Percentage, fixedAmount: null, rateBps: 150, minCharge: 10m, maxCharge: 1_000m);
        percentage.Calculate(5_000m).ShouldBe(75m);
        percentage.Calculate(100m).ShouldBe(10m, "1.5% of 100 is below the minimum cap");
        percentage.Calculate(100_000m).ShouldBe(1_000m, "capped at the maximum");
        percentage.Describe().ShouldBe("1.5% (10–1,000)");

        var tiered = Rule(charge: FeeChargeType.Tiered, fixedAmount: null, tiers: [(1_000m, 30m), (5_000m, 50m), (null, 110m)]);
        tiered.Calculate(1_000m).ShouldBe(30m, "a band includes its upper limit");
        tiered.Calculate(1_000.01m).ShouldBe(50m);
        tiered.Calculate(80_000m).ShouldBe(110m, "the open-ended last band");
    }

    [Fact]
    public void A_bounded_tariff_refuses_amounts_beyond_its_last_band()
    {
        var tiered = Rule(charge: FeeChargeType.Tiered, fixedAmount: null, tiers: [(1_000m, 30m)]);
        Should.Throw<DomainRuleException>(() => tiered.Calculate(2_000m)).Code.ShouldBe("savings.fees.no_tier");
    }

    [Fact]
    public void Charge_shapes_are_validated()
    {
        Should.Throw<DomainRuleException>(() => Rule(fixedAmount: 45m, maxCharge: 50m, approve: false)).Code.ShouldBe("savings.fees.fixed_only_amount");
        Should.Throw<DomainRuleException>(() => Rule(charge: FeeChargeType.Percentage, fixedAmount: null, rateBps: 0, approve: false)).Code.ShouldBe("savings.fees.rate_invalid");
        Should.Throw<DomainRuleException>(() => Rule(charge: FeeChargeType.Tiered, fixedAmount: null, tiers: [(5_000m, 50m), (1_000m, 30m)], approve: false)).Code.ShouldBe("savings.fees.tiers_not_ascending");
        Should.Throw<DomainRuleException>(() => Rule(charge: FeeChargeType.Tiered, fixedAmount: null, tiers: [(null, 50m), (1_000m, 30m)], approve: false)).Code.ShouldBe("savings.fees.tier_open_not_last");
        Should.Throw<DomainRuleException>(() => Rule(min: 500m, max: 100m, approve: false)).Code.ShouldBe("savings.fees.range_invalid");
        Should.Throw<DomainRuleException>(() => Rule(charge: FeeChargeType.Percentage, fixedAmount: null, rateBps: 100, minCharge: 50m, maxCharge: 10m, approve: false)).Code.ShouldBe("savings.fees.caps_invalid");
    }

    [Fact]
    public void A_balance_enquiry_fee_is_fixed_per_product_with_no_channel()
    {
        Should.Throw<DomainRuleException>(() => Rule(FeeTransactionType.BalanceEnquiry, channel: FeeChannel.MPesa, approve: false)).Code.ShouldBe("savings.fees.enquiry_no_channel");
        Should.Throw<DomainRuleException>(() => Rule(FeeTransactionType.BalanceEnquiry, channel: null, product: null, approve: false)).Code.ShouldBe("savings.fees.enquiry_product_required");
        Should.Throw<DomainRuleException>(() => Rule(FeeTransactionType.BalanceEnquiry, channel: null, charge: FeeChargeType.Percentage, fixedAmount: null, rateBps: 100, approve: false))
            .Code.ShouldBe("savings.fees.enquiry_fixed_only");

        var enquiry = Rule(FeeTransactionType.BalanceEnquiry, channel: null, fixedAmount: 10m);
        enquiry.MaxAmount.ShouldBeNull();
        enquiry.Applies(FeeTransactionType.BalanceEnquiry, null, "fosa-cur", 0m).ShouldBeTrue("product codes compare case-insensitively and amount is irrelevant");
    }

    [Fact]
    public void A_rule_applies_only_when_live_and_matching_and_the_more_specific_rule_takes_precedence()
    {
        var pending = Rule(approve: false);
        pending.Applies(FeeTransactionType.Withdrawal, FeeChannel.MPesa, "FOSA-CUR", 1_000m).ShouldBeFalse("not approved yet");

        var specific = Rule();
        specific.Applies(FeeTransactionType.Withdrawal, FeeChannel.MPesa, "FOSA-CUR", 1_000m).ShouldBeTrue();
        specific.Applies(FeeTransactionType.Withdrawal, FeeChannel.AirtelMoney, "FOSA-CUR", 1_000m).ShouldBeFalse();
        specific.Applies(FeeTransactionType.Withdrawal, FeeChannel.MPesa, "FOSA-CUR", 200_000m).ShouldBeFalse("outside the amount range");
        specific.Applies(FeeTransactionType.Deposit, FeeChannel.MPesa, "FOSA-CUR", 1_000m).ShouldBeFalse();

        var anyChannelAnyProduct = Rule(channel: null, product: null);
        anyChannelAnyProduct.Applies(FeeTransactionType.Withdrawal, FeeChannel.Cash, "BOSA-DEP", 1_000m).ShouldBeTrue();
        specific.Precedence.Specificity.ShouldBeGreaterThan(anyChannelAnyProduct.Precedence.Specificity);
    }

    [Fact]
    public void Overlap_is_same_type_channel_and_product_with_intersecting_ranges()
    {
        var existing = Rule(min: 1m, max: 5_000m);
        Rule(min: 5_000m, max: 10_000m, approve: false).Overlaps(existing).ShouldBeTrue("ranges share 5,000");
        Rule(min: 5_000.01m, max: 10_000m, approve: false).Overlaps(existing).ShouldBeFalse();
        Rule(channel: FeeChannel.AirtelMoney, approve: false).Overlaps(existing).ShouldBeFalse("a different channel");
        Rule(product: null, approve: false).Overlaps(existing).ShouldBeFalse("any-product is a different, less specific slot");
    }

    [Fact]
    public void Approval_is_maker_checker_and_only_live_rules_deactivate()
    {
        var rule = Rule(approve: false);
        Should.Throw<MakerCheckerViolationException>(() => rule.Approve(Maker, Now));
        Should.Throw<DomainRuleException>(() => rule.Deactivate(Checker, Now)).Code.ShouldBe("savings.fees.not_active");
        rule.Approve(Checker, Now);
        rule.Status.ShouldBe(FeeRuleStatus.Active);
        rule.Deactivate(Checker, Now);
        rule.Status.ShouldBe(FeeRuleStatus.Inactive);

        var rejected = Rule(approve: false);
        Should.Throw<DomainRuleException>(() => rejected.Reject(Checker, " ", Now)).Code.ShouldBe("savings.fees.reason_required");
        rejected.Reject(Checker, "Tariff not board-approved", Now);
        rejected.Status.ShouldBe(FeeRuleStatus.Rejected);
    }
}
