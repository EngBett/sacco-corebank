using Sacco.Modules.Savings.Application;
using Shouldly;

namespace Sacco.UnitTests.Savings;

public sealed class DividendProRataTests
{
    private static readonly DateOnly Jan1 = new(2025, 1, 1);
    private static readonly DateOnly Dec31 = new(2025, 12, 31);

    [Fact]
    public void Constant_balance_averages_to_itself()
        => DividendService.AverageDailyBalance(50_000m, [], Jan1, Dec31).ShouldBe(50_000m);

    [Fact]
    public void A_deposit_in_december_earns_a_twelfth_of_one_in_january()
    {
        var january = DividendService.AverageDailyBalance(0m, [(new DateOnly(2025, 1, 1), 120_000m)], Jan1, Dec31);
        var december = DividendService.AverageDailyBalance(0m, [(new DateOnly(2025, 12, 1), 120_000m)], Jan1, Dec31);
        january.ShouldBe(120_000m);
        december.ShouldBe(decimal.Round(120_000m * 31 / 365, 2));
    }

    [Fact]
    public void Multiple_movements_on_one_day_use_the_closing_balance_of_that_day()
    {
        var avg = DividendService.AverageDailyBalance(10_000m, [(new DateOnly(2025, 7, 1), 20_000m), (new DateOnly(2025, 7, 1), 5_000m)], Jan1, Dec31);
        // 181 days at 10,000 then 184 days at 5,000
        avg.ShouldBe(decimal.Round((10_000m * 181 + 5_000m * 184) / 365, 2));
    }

    [Fact]
    public void Movements_outside_the_period_are_ignored()
        => DividendService.AverageDailyBalance(1_000m, [(new DateOnly(2026, 2, 1), 999_999m)], Jan1, Dec31).ShouldBe(1_000m);
}
