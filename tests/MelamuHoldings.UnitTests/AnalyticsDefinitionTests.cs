using MelamuHoldings.Application.Trading;
using static MelamuHoldings.UnitTests.TradeFactory;

namespace MelamuHoldings.UnitTests;

// P8.4: two definitions change. Profit factor is undefined with no losses, and the equity curve's drawdown is an amount.
// The tests marked "unchanged" passed against the code before the change, with figures worked out by hand: every other
// figure is pinned, so a fix to these two cannot move anything else.
public class AnalyticsDefinitionTests
{
    // Monday 5 to Friday 9 October 2026. Trade by trade the running total is 500, 300, -100, 0, 1000, 900, 600.
    private static AnalyticsService Week() => new(new InMemoryTradeRepository(
        Trade("2026-10-05", "09:00", 500, rr: 2.5m, strategy: "ORB"),
        Trade("2026-10-05", "10:00", -200, rr: -1, strategy: "ORB", session: "London"),
        Trade("2026-10-06", "09:00", -400, rr: -2, strategy: "Pullback"),
        Trade("2026-10-07", "09:00", 100, rr: 0.5m, strategy: "Pullback", session: "London"),
        Trade("2026-10-08", "09:00", 1000, rr: 5, strategy: "ORB"),
        Trade("2026-10-08", "10:00", -100, rr: -0.5m, strategy: "Pullback"),
        Trade("2026-10-09", "09:00", -300, rr: -1.5m, strategy: "ORB")));

    private static AnalyticsService Results(params decimal[] pnls) =>
        new(new InMemoryTradeRepository([.. pnls.Select((pnl, i) => Trade("2026-10-05", $"{9 + i:00}:00", pnl))]));

    [Fact]
    public async Task Unchanged_every_summary_figure_when_there_are_losses()
    {
        var s = await Week().GetSummaryAsync(1);

        Assert.Equal((7, 3, 4, 0), (s.TotalTrades, s.WinCount, s.LossCount, s.BreakevenCount));
        Assert.Equal(42.86m, s.WinRate);        // 3 of 7
        Assert.Equal(85.71m, s.Expectancy);     // 600 / 7
        Assert.Equal(0.43m, s.AverageRR);       // 3 / 7
        Assert.Equal(1.60m, s.ProfitFactor);    // 1600 / 1000
        Assert.Equal(600m, s.TotalPnL);
        Assert.Equal(600m, s.MaxDrawdown);      // 500 down to -100
        Assert.Equal((20, true), (s.MinimumSample, s.InsufficientData));
    }

    [Fact]
    public async Task Unchanged_the_equity_curves_dates_and_totals()
    {
        var points = (await Week().GetEquityCurveAsync(1)).ToList();

        Assert.Equal(["2026-10-05", "2026-10-06", "2026-10-07", "2026-10-08", "2026-10-09"], points.Select(p => p.Date.ToString("yyyy-MM-dd")));
        Assert.Equal([300m, -400m, 100m, 900m, -300m], points.Select(p => p.DailyPnL));
        Assert.Equal([300m, -100m, 0m, 900m, 600m], points.Select(p => p.CumulativePnL));
    }

    [Fact]
    public async Task Unchanged_strategy_figures_when_there_are_losses()
    {
        var rows = (await Week().GetStrategyPerformanceAsync(1)).ToList();

        Assert.Equal(("ORB", 4, 50m, 1.25m, 1000m, 3m), (rows[0].StrategyName, rows[0].TotalTrades, rows[0].WinRate, rows[0].AverageRR, rows[0].TotalPnL, rows[0].ProfitFactor));
        Assert.Equal(("Pullback", 3, 33.33m, -0.67m, -400m, 0.2m), (rows[1].StrategyName, rows[1].TotalTrades, rows[1].WinRate, rows[1].AverageRR, rows[1].TotalPnL, rows[1].ProfitFactor));
    }

    [Fact]
    public async Task Unchanged_figures_by_day_and_by_session()
    {
        var days = (await Week().GetPerformanceByDayAsync(1)).ToList();
        var sessions = (await Week().GetPerformanceBySessionAsync(1)).ToList();

        Assert.Equal(
            [(2, 50m, 900m, 2.25m), (2, 50m, 300m, 0.75m), (1, 100m, 100m, 0.5m), (1, 0m, -300m, -1.5m), (1, 0m, -400m, -2m)],
            days.Select(d => (d.TotalTrades, d.WinRate, d.TotalPnL, d.AverageRR)));
        Assert.Equal(
            [("New York", 5, 40m, 700m, 0.7m), ("London", 2, 50m, -100m, -0.25m)],
            sessions.Select(r => (r.Label, r.TotalTrades, r.WinRate, r.TotalPnL, r.AverageRR)));
    }

    [Fact]
    public async Task Unchanged_a_profit_factor_with_losses_and_no_wins_is_zero()
    {
        Assert.Equal(0m, (await Results(-100, -50).GetSummaryAsync(1)).ProfitFactor);
    }

    [Fact]
    public async Task Profit_factor_is_undefined_with_no_losses_rather_than_the_gross_profit()
    {
        var s = await Results(150, 50).GetSummaryAsync(1);

        Assert.Null(s.ProfitFactor);
        Assert.Equal((200m, 2, 100m), (s.TotalPnL, s.WinCount, s.WinRate));
    }

    [Fact]
    public async Task Profit_factor_is_undefined_when_every_trade_broke_even_and_when_there_are_no_trades()
    {
        Assert.Null((await Results(0, 0).GetSummaryAsync(1)).ProfitFactor);
        Assert.Null((await new AnalyticsService(new InMemoryTradeRepository()).GetSummaryAsync(1)).ProfitFactor);
    }

    [Fact]
    public async Task A_breakeven_does_not_make_profit_factor_defined()
    {
        Assert.Null((await Results(100, 0).GetSummaryAsync(1)).ProfitFactor);
    }

    [Fact]
    public async Task A_strategy_with_no_losses_has_an_undefined_profit_factor_and_keeps_its_other_figures()
    {
        var row = Assert.Single(await new AnalyticsService(new InMemoryTradeRepository(
            Trade("2026-10-05", "09:00", 150, rr: 1.5m, strategy: "Clean"),
            Trade("2026-10-05", "10:00", 50, rr: 0.5m, strategy: "Clean"))).GetStrategyPerformanceAsync(1));

        Assert.Null(row.ProfitFactor);
        Assert.Equal(("Clean", 2, 100m, 1m, 200m), (row.StrategyName, row.TotalTrades, row.WinRate, row.AverageRR, row.TotalPnL));
    }

    [Fact]
    public async Task The_curves_drawdown_is_the_amount_below_the_highest_total_so_far()
    {
        var points = (await Week().GetEquityCurveAsync(1)).ToList();

        // Peaks by day: 300, 300, 300, 900, 900. As a percentage the second day read 133.33%, which says nothing.
        Assert.Equal([0m, 400m, 300m, 0m, 300m], points.Select(p => p.Drawdown));
    }

    [Fact]
    public async Task A_loss_before_any_profit_is_a_drawdown_from_zero_as_max_drawdown_counts_it()
    {
        var service = new AnalyticsService(new InMemoryTradeRepository(
            Trade("2026-10-05", "09:00", -100),
            Trade("2026-10-06", "09:00", 40)));

        Assert.Equal([100m, 60m], (await service.GetEquityCurveAsync(1)).Select(p => p.Drawdown));
        Assert.Equal(100m, (await service.GetSummaryAsync(1)).MaxDrawdown);
    }

    [Fact]
    public async Task The_curve_reads_day_ends_so_max_drawdown_can_be_larger_because_it_reads_each_trade()
    {
        var curve = await Week().GetEquityCurveAsync(1);
        var summary = await Week().GetSummaryAsync(1);

        Assert.Equal(400m, curve.Max(p => p.Drawdown));
        Assert.Equal(600m, summary.MaxDrawdown);
    }

    [Fact]
    public async Task The_curves_drawdown_is_rounded_to_cents()
    {
        var service = new AnalyticsService(new InMemoryTradeRepository(
            Trade("2026-10-05", "09:00", 100.126m),
            Trade("2026-10-06", "09:00", -50.111m)));

        Assert.Equal(50.11m, (await service.GetEquityCurveAsync(1)).Last().Drawdown);
    }
}
