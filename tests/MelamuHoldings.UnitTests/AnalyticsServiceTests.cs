using MelamuHoldings.Application.Trading;
using static MelamuHoldings.UnitTests.TradeFactory;

namespace MelamuHoldings.UnitTests;

public class AnalyticsServiceTests
{
    // Two wins (+200, +300), two losses (-100, -100) and one breakeven across three days
    private static AnalyticsService ServiceWithMixedResults() => new(new InMemoryTradeRepository(
        Trade("2026-10-05", "09:00", 200, rr: 2, strategy: "ORB"),
        Trade("2026-10-05", "10:00", -100, rr: -1, strategy: "ORB"),
        Trade("2026-10-06", "09:00", -100, rr: -1),
        Trade("2026-10-06", "10:00", 300, rr: 3),
        Trade("2026-10-07", "09:00", 0, rr: 0)));

    [Fact]
    public async Task Summary_counts_wins_losses_and_breakevens()
    {
        var summary = await ServiceWithMixedResults().GetSummaryAsync(userId: 1);

        Assert.Equal(5, summary.TotalTrades);
        Assert.Equal(2, summary.WinCount);
        Assert.Equal(2, summary.LossCount);
        Assert.Equal(1, summary.BreakevenCount);
        Assert.Equal(40.00m, summary.WinRate);
        Assert.Equal(300.00m, summary.TotalPnL);
        Assert.Equal(0.60m, summary.AverageRR);
    }

    [Fact]
    public async Task Expectancy_equals_mean_pnl_per_trade_and_ignores_breakevens_as_losses()
    {
        var summary = await ServiceWithMixedResults().GetSummaryAsync(userId: 1);

        // 0.4 × 250 − 0.4 × 100 = 60 = 300 / 5. Before the fix, breakevens were counted as losses (result 40).
        Assert.Equal(60.00m, summary.Expectancy);
    }

    [Fact]
    public async Task Profit_factor_is_gross_profit_over_gross_loss()
    {
        var summary = await ServiceWithMixedResults().GetSummaryAsync(userId: 1);

        Assert.Equal(2.50m, summary.ProfitFactor);
    }

    [Fact]
    public async Task Profit_factor_with_no_losses_currently_returns_gross_profit()
    {
        // Characterises a known defect (ARCHITECTURE_REVIEW W13): the ratio is undefined with no losses,
        // but the API returns the gross profit amount. Fixing it changes the DTO contract (Phase 8).
        var service = new AnalyticsService(new InMemoryTradeRepository(
            Trade("2026-10-05", "09:00", 150),
            Trade("2026-10-05", "10:00", 50)));

        var summary = await service.GetSummaryAsync(userId: 1);

        Assert.Equal(200m, summary.ProfitFactor);
    }

    [Fact]
    public async Task Max_drawdown_is_largest_peak_to_trough_drop_in_cumulative_pnl()
    {
        var summary = await ServiceWithMixedResults().GetSummaryAsync(userId: 1);

        // Cumulative: 200, 100, 0, 300, 300 → peak 200 to trough 0
        Assert.Equal(200.00m, summary.MaxDrawdown);
    }

    [Fact]
    public async Task Summary_is_empty_when_user_has_no_trades()
    {
        var service = new AnalyticsService(new InMemoryTradeRepository(Trade("2026-10-05", "09:00", 100, userId: 2)));

        var summary = await service.GetSummaryAsync(userId: 1);

        Assert.Equal(0, summary.TotalTrades);
        Assert.Equal(0m, summary.TotalPnL);
    }

    [Fact]
    public async Task Equity_curve_has_one_point_per_day_with_running_total()
    {
        var points = (await ServiceWithMixedResults().GetEquityCurveAsync(userId: 1)).ToList();

        Assert.Equal(3, points.Count);
        Assert.Equal([100m, 200m, 0m], points.Select(p => p.DailyPnL));
        Assert.Equal([100m, 300m, 300m], points.Select(p => p.CumulativePnL));
    }

    [Fact]
    public async Task Strategy_performance_groups_untagged_trades_and_orders_by_pnl()
    {
        var strategies = (await ServiceWithMixedResults().GetStrategyPerformanceAsync(userId: 1)).ToList();

        Assert.Equal(["Untagged", "ORB"], strategies.Select(s => s.StrategyName));
        var orb = strategies.Single(s => s.StrategyName == "ORB");
        Assert.Equal(2, orb.TotalTrades);
        Assert.Equal(50.00m, orb.WinRate);
        Assert.Equal(100.00m, orb.TotalPnL);
    }
}
