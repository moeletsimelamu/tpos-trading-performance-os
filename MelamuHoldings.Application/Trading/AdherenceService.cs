using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

// How often trades followed their strategy's mandatory rules, and how those trades turned out next to the rest.
// Only closed and reviewed trades count (as everywhere in analytics), and only complete checklists are scored.
// The comparison describes what happened; it does not claim the rules caused the difference.
public class AdherenceService
{
    // Below this many trades a group's results are labelled as insufficient data
    public const int MinimumSample = 20;

    private readonly ITradeRepository _trades;
    private readonly IStrategyRepository _strategies;

    public AdherenceService(ITradeRepository trades, IStrategyRepository strategies)
    {
        _trades = trades;
        _strategies = strategies;
    }

    public async Task<AdherenceReportDto> GetReportAsync(int userId, int? strategyId = null)
    {
        var trades = (await _trades.GetByUserIdAsync(userId))
            .Where(t => t.CountsInAnalytics && t.PnL is not null && (strategyId is null || t.StrategyId == strategyId))
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
                return new RuleViolationDto(g.Key, latest.Trade.Strategy?.Name ?? string.Empty, latest.Item.Text,
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

    private static AdherenceGroupDto Results(IReadOnlyList<Trade> trades)
    {
        var wins = trades.Count(t => t.PnL > 0);
        var withR = trades.Where(t => t.RR is not null).ToList();
        return new AdherenceGroupDto(
            trades.Count, wins, Math.Round(trades.Sum(t => t.PnL!.Value), 2), Rate(wins, trades.Count),
            withR.Count == 0 ? null : Math.Round(withR.Average(t => t.RR!.Value), 2),
            InsufficientData: trades.Count < MinimumSample);
    }
}
