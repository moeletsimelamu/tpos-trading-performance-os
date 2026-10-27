using MelamuHoldings.Application.Services;
using MelamuHoldings.Domain.Entities;
using static MelamuHoldings.UnitTests.TradeFactory;

namespace MelamuHoldings.UnitTests;

public class BehaviourAnalyticsServiceTests
{
    private static Task<BehaviourSummaryDto> Analyse(params Trade[] trades) =>
        new BehaviourAnalyticsService(new InMemoryTradeRepository(trades)).GetBehaviourSummaryAsync(userId: 1);

    [Fact]
    public async Task Flags_revenge_trade_entered_within_20_minutes_of_a_loss()
    {
        var summary = await Analyse(
            Trade("2026-10-06", "09:40", -400),
            Trade("2026-10-06", "09:55", 200));

        var alert = Assert.Single(summary.Alerts);
        Assert.Equal("RevengeTrading", alert.AlertType);
        Assert.Equal(1, summary.RevengeTradingCount);
    }

    [Fact]
    public async Task Does_not_flag_trade_entered_more_than_20_minutes_after_a_loss()
    {
        var summary = await Analyse(
            Trade("2026-10-06", "09:40", -400),
            Trade("2026-10-06", "10:05", 200));

        Assert.Equal(0, summary.RevengeTradingCount);
    }

    [Theory]
    [InlineData(5, 0, null)]
    [InlineData(6, 1, "Warning")]
    [InlineData(11, 1, "Critical")]
    public async Task Flags_overtrading_above_five_trades_per_day(int tradeCount, int expectedDays, string? expectedSeverity)
    {
        // Hourly wins so no other detector fires
        var trades = Enumerable.Range(0, tradeCount)
            .Select(i => Trade("2026-10-12", $"{8 + i:00}:00", 50))
            .ToArray();

        var summary = await Analyse(trades);

        Assert.Equal(expectedDays, summary.OvertradingDayCount);
        if (expectedSeverity is not null)
            Assert.Equal(expectedSeverity, summary.Alerts.Single(a => a.AlertType == "Overtrading").Severity);
    }

    [Fact]
    public async Task Flags_streak_of_three_losses_and_reports_longest_streak()
    {
        var summary = await Analyse(
            Trade("2026-10-07", "09:00", -100),
            Trade("2026-10-08", "09:00", -100),
            Trade("2026-10-09", "09:00", -100),
            Trade("2026-10-10", "09:00", 300),
            Trade("2026-10-11", "09:00", -100));

        var alert = Assert.Single(summary.Alerts, a => a.AlertType == "ConsecutiveLosses");
        Assert.Equal("Warning", alert.Severity);
        Assert.Equal(3, alert.TradeIds.Count);
        Assert.Equal(3, summary.MaxConsecutiveLosses);
    }

    [Fact]
    public async Task Five_loss_streak_at_end_of_history_is_critical()
    {
        var trades = Enumerable.Range(1, 5).Select(d => Trade($"2026-10-{d:00}", "09:00", -50)).ToArray();

        var summary = await Analyse(trades);

        Assert.Equal("Critical", summary.Alerts.Single(a => a.AlertType == "ConsecutiveLosses").Severity);
    }

    [Fact]
    public async Task Reports_count_and_pnl_of_manual_overrides()
    {
        var summary = await Analyse(
            Trade("2026-10-07", "09:00", 200, manualOverride: true),
            Trade("2026-10-08", "09:00", -350, manualOverride: true),
            Trade("2026-10-09", "09:00", 500));

        Assert.Equal(2, summary.StrategyDeviationCount);
        Assert.Equal(-150.00m, summary.DeviationPnLImpact);
    }
}
