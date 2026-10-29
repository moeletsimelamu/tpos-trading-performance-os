using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// How often trades followed their strategy's mandatory rules, and how those trades turned out next to the rest.
// Only closed and reviewed trades count (as everywhere in analytics), and only complete checklists are scored.
// The comparison describes what happened; it does not claim the rules caused the difference.
public class AdherenceService
{
    // Below this many trades a group's results are labelled as insufficient data
    public const int MinimumSample = 20;

    private readonly AnalyticsScope _scope;
    private readonly IStrategyRepository _strategies;

    public AdherenceService(AnalyticsScope scope, IStrategyRepository strategies)
    {
        _scope = scope;
        _strategies = strategies;
    }

    // The same trades as every other analytics figure: the filter (which may name one strategy) narrows them
    public async Task<AdherenceReportDto> GetReportAsync(int userId, AnalyticsFilter? filter = null)
    {
        var trades = (await _scope.TradesAsync(userId, filter))
            .Where(t => t.CountsInAnalytics && t.PnL is not null)
            .ToList();
        var checklists = await _strategies.GetChecklistsAsync(trades.Select(t => t.Id).ToList());

        // Each trade is scored on the checklist of its current strategy
        var scored = trades.Select(t => (Trade: t, Checklist: checklists.FirstOrDefault(c => c.TradeId == t.Id && c.StrategyId == t.StrategyId))).ToList();
        var complete = scored.Where(s => s.Checklist is { IsComplete: true }).ToList();
        var compliant = complete.Where(s => s.Checklist!.IsCompliant == true).Select(s => s.Trade).ToList();
        var nonCompliant = complete.Where(s => s.Checklist!.IsCompliant == false).Select(s => s.Trade).ToList();

        var violations = complete
            .SelectMany(s => s.Checklist!.Items.Where(i => i.IsMandatory).Select(i => (s.Trade, Item: i)))
            .GroupBy(x => x.Item.StrategyRuleId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.Trade.TradeDate).First();
                return new RuleViolationDto(g.Key, latest.Trade.Strategy?.Name ?? string.Empty, latest.Item.Text, latest.Item.Category,
                    g.Count(x => x.Item.Current!.Answer == RuleAnswer.NotMet), g.Count());
            })
            .Where(v => v.TimesNotMet > 0)
            .OrderByDescending(v => v.TimesNotMet).ThenBy(v => v.Text)
            .Take(5)
            .ToList();

        var byStrategy = scored
            .Where(s => s.Trade.StrategyId is not null)
            .GroupBy(s => s.Trade.StrategyId!.Value)
            .Select(g =>
            {
                var withChecklist = g.Count(s => s.Checklist is { IsComplete: true });
                var ok = g.Count(s => s.Checklist is { IsCompliant: true });
                return new StrategyAdherenceDto(g.Key, g.First().Trade.Strategy?.Name ?? string.Empty, g.Count(), withChecklist, ok, Rate(ok, withChecklist));
            })
            .OrderBy(s => s.StrategyName)
            .ToList();

        return new AdherenceReportDto(
            CompletedTrades: trades.Count,
            WithChecklist: complete.Count,
            IncompleteChecklist: scored.Count(s => s.Checklist is { IsComplete: false }),
            WithoutChecklist: scored.Count(s => s.Checklist is null),
            Compliant: compliant.Count,
            NonCompliant: nonCompliant.Count,
            AdherenceRate: Rate(compliant.Count, complete.Count),
            MinimumSample: MinimumSample,
            CompliantResults: Results(compliant),
            NonCompliantResults: Results(nonCompliant),
            MostViolated: violations,
            ByStrategy: byStrategy);
    }

    // Percentage with two decimals, like the win rate on the Performance page; none when there is no denominator
    private static decimal? Rate(int part, int whole) => whole == 0 ? null : Math.Round((decimal)part / whole * 100, 2);

    // How a group of completed trades turned out; shared with the journal-answer breakdown so both count alike
    internal static AdherenceGroupDto Results(IReadOnlyList<Trade> trades)
    {
        var wins = trades.Count(t => t.PnL > 0);
        var withR = trades.Where(t => t.RR is not null).ToList();
        return new AdherenceGroupDto(
            trades.Count, wins, Math.Round(trades.Sum(t => t.PnL!.Value), 2), Rate(wins, trades.Count),
            withR.Count == 0 ? null : Math.Round(withR.Average(t => t.RR!.Value), 2),
            InsufficientData: trades.Count < MinimumSample);
    }

    // At most this many periods are returned, the most recent ones
    public const int MaxPeriods = 104;

    // Adherence period by period, from the first to the last completed trade in view, empty periods included so a
    // gap in trading shows as a gap. Each period is scored like the report: complete checklists only.
    public async Task<AdherenceTrendDto> GetTrendAsync(int userId, TrendPeriod period, AnalyticsFilter? filter = null)
    {
        var trades = (await _scope.TradesAsync(userId, filter)).Where(t => t.CountsInAnalytics && t.PnL is not null).ToList();
        if (trades.Count == 0) return new AdherenceTrendDto(period, MinimumSample, false, []);
        var checklists = await _strategies.GetChecklistsAsync(trades.Select(t => t.Id).ToList());
        var scored = trades.Select(t => (Trade: t, Checklist: checklists.FirstOrDefault(c => c.TradeId == t.Id && c.StrategyId == t.StrategyId))).ToList();

        DateOnly Start(DateOnly d) => period == TrendPeriod.Week
            ? d.AddDays(-(((int)d.DayOfWeek + 6) % 7))   // weeks start on Monday
            : new DateOnly(d.Year, d.Month, 1);
        DateOnly Next(DateOnly d) => period == TrendPeriod.Week ? d.AddDays(7) : d.AddMonths(1);

        var periods = new List<AdherencePeriodDto>();
        for (var start = Start(trades.Min(t => t.TradeDate)); start <= trades.Max(t => t.TradeDate); start = Next(start))
        {
            var end = Next(start).AddDays(-1);
            var inPeriod = scored.Where(s => s.Trade.TradeDate >= start && s.Trade.TradeDate <= end).ToList();
            var complete = inPeriod.Count(s => s.Checklist is { IsComplete: true });
            var compliant = inPeriod.Count(s => s.Checklist is { IsComplete: true, IsCompliant: true });
            periods.Add(new AdherencePeriodDto(start, end, inPeriod.Count, complete, compliant, Rate(compliant, complete), complete < MinimumSample));
        }
        var truncated = periods.Count > MaxPeriods;
        return new AdherenceTrendDto(period, MinimumSample, truncated, truncated ? periods[^MaxPeriods..] : periods);
    }
}
