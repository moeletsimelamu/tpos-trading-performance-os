using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public record TradeSummaryDto(
    int Id, TradeStatus Status, DateOnly TradeDate, string Instrument, string? Direction, string? StrategyName,
    decimal? PnL, decimal? RR, bool IsImported, DateTime LastChangedAt);

// What needs attention in the journal, and what changed last
public record JournalOverviewDto(
    int Drafts, int Open, int AwaitingReview, int Reviewed,
    IReadOnlyList<TradeSummaryDto> DraftTrades, IReadOnlyList<TradeSummaryDto> OpenTrades,
    IReadOnlyList<TradeSummaryDto> AwaitingReviewTrades, IReadOnlyList<TradeSummaryDto> Recent);

// One day of the calendar. P&L and wins count closed and reviewed trades only, as every analytics figure does;
// drafts and open positions are counted beside them so a day with only unfinished trades still shows.
public record CalendarDayDto(DateOnly Date, int CompletedTrades, decimal NetPnL, int Wins, int Losses, int Drafts, int Open);

public record CalendarMonthDto(int Year, int Month, int CompletedTrades, decimal NetPnL, IReadOnlyList<CalendarDayDto> Days);

public class JournalOverviewService
{
    // How many trades each list shows; the counts are always complete
    public const int ListSize = 8;

    private readonly ITradeRepository _trades;

    public JournalOverviewService(ITradeRepository trades) => _trades = trades;

    public async Task<JournalOverviewDto> GetOverviewAsync(int userId)
    {
        var trades = (await _trades.GetByUserIdAsync(userId)).ToList();
        List<Trade> With(TradeStatus status) => trades.Where(t => t.Status == status).ToList();
        var drafts = With(TradeStatus.Draft);
        var open = With(TradeStatus.Open);
        var awaiting = With(TradeStatus.Closed);

        return new JournalOverviewDto(
            drafts.Count, open.Count, awaiting.Count, With(TradeStatus.Reviewed).Count,
            Latest(drafts), Latest(open), Latest(awaiting),
            trades.OrderByDescending(LastChanged).ThenByDescending(t => t.Id).Take(ListSize).Select(Summary).ToList());
    }

    public async Task<CalendarMonthDto> GetMonthAsync(int userId, int year, int month)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new DomainException("Choose a month between January 2000 and December 2100.");

        var first = new DateOnly(year, month, 1);
        var trades = (await _trades.GetByUserIdAndDateRangeAsync(userId, first, first.AddMonths(1).AddDays(-1))).ToList();
        var completed = CompletedTrade.From(trades);

        var days = trades.GroupBy(t => t.TradeDate).OrderBy(g => g.Key).Select(g =>
        {
            var done = completed.Where(c => c.TradeDate == g.Key).ToList();
            return new CalendarDayDto(g.Key, done.Count, done.Sum(c => c.PnL), done.Count(c => c.PnL > 0), done.Count(c => c.PnL < 0),
                g.Count(t => t.Status == TradeStatus.Draft), g.Count(t => t.Status == TradeStatus.Open));
        }).ToList();

        return new CalendarMonthDto(year, month, completed.Count, completed.Sum(c => c.PnL), days);
    }

    private static List<TradeSummaryDto> Latest(IEnumerable<Trade> trades) =>
        trades.OrderByDescending(t => t.TradeDate).ThenByDescending(t => t.Id).Take(ListSize).Select(Summary).ToList();

    private static DateTime LastChanged(Trade t) => t.UpdatedAt ?? t.CreatedAt;

    internal static TradeSummaryDto Summary(Trade t) => new(
        t.Id, t.Status, t.TradeDate, t.Instrument, t.Direction?.ToString(), t.Strategy?.Name, t.PnL, t.RR, t.IsImported, LastChanged(t));
}
