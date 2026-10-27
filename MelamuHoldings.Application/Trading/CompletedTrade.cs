using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// The one place that decides which trades analytics see: closed and reviewed trades, never drafts or open
// positions. Every figure on the Performance and Behaviour pages is calculated from this projection.
public sealed record CompletedTrade(
    int Id,
    DateOnly TradeDate,
    TimeOnly EntryTime,
    decimal PnL,
    decimal? RR,
    string Session,
    string DayOfWeek,
    string? StrategyName,
    bool IsManualOverride)
{
    public static List<CompletedTrade> From(IEnumerable<Trade> trades) => trades
        .Where(t => t.CountsInAnalytics && t.PnL is not null)
        .Select(t => new CompletedTrade(
            t.Id, t.TradeDate, t.EntryTime ?? TimeOnly.MinValue, t.PnL!.Value, t.RR,
            t.Session, t.DayOfWeek, t.Strategy?.Name, t.IsManualOverride))
        .ToList();
}
