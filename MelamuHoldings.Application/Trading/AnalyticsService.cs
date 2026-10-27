using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public class AnalyticsSummaryDto
{
    public int TotalTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal Expectancy { get; set; }
    public decimal AverageRR { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal MaxDrawdown { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public int BreakevenCount { get; set; }
}

public class EquityPointDto
{
    public DateOnly Date { get; set; }
    public decimal CumulativePnL { get; set; }
    public decimal DailyPnL { get; set; }
    public decimal Drawdown { get; set; }
}

public class StrategyPerformanceDto
{
    public string StrategyName { get; set; } = string.Empty;
    public int TotalTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal AverageRR { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal ProfitFactor { get; set; }
}

public class TimePerformanceDto
{
    public string Label { get; set; } = string.Empty;
    public int TotalTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal TotalPnL { get; set; }
    public decimal AverageRR { get; set; }
}

public class AnalyticsService
{
    private readonly ITradeRepository _tradeRepository;

    public AnalyticsService(ITradeRepository tradeRepository)
    {
        _tradeRepository = tradeRepository;
    }

    public async Task<AnalyticsSummaryDto> GetSummaryAsync(int userId)
    {
        var trades = (await _tradeRepository.GetByUserIdAsync(userId)).ToList();
        if (trades.Count == 0) return new AnalyticsSummaryDto();

        var wins = trades.Where(t => t.PnL > 0).ToList();
        var losses = trades.Where(t => t.PnL < 0).ToList();
        var breakevens = trades.Where(t => t.PnL == 0).ToList();

        var grossProfit = wins.Sum(t => t.PnL);
        var grossLoss = Math.Abs(losses.Sum(t => t.PnL));

        var winRate = (decimal)wins.Count / trades.Count;
        var avgWin = wins.Count > 0 ? grossProfit / wins.Count : 0;
        var avgLoss = losses.Count > 0 ? grossLoss / losses.Count : 0;

        // Expectancy = (WinRate × AvgWin) - (LossRate × AvgLoss)
        // LossRate counts losing trades only; breakevens contribute zero, so this equals the mean P&L per trade
        var lossRate = (decimal)losses.Count / trades.Count;
        var expectancy = (winRate * avgWin) - (lossRate * avgLoss);

        return new AnalyticsSummaryDto
        {
            TotalTrades = trades.Count,
            WinCount = wins.Count,
            LossCount = losses.Count,
            BreakevenCount = breakevens.Count,
            WinRate = Math.Round(winRate * 100, 2),
            Expectancy = Math.Round(expectancy, 2),
            AverageRR = Math.Round(trades.Average(t => t.RR), 2),
            ProfitFactor = grossLoss == 0 ? grossProfit : Math.Round(grossProfit / grossLoss, 2),
            TotalPnL = Math.Round(trades.Sum(t => t.PnL), 2),
            MaxDrawdown = Math.Round(CalculateMaxDrawdown(trades), 2)
        };
    }

    public async Task<IEnumerable<EquityPointDto>> GetEquityCurveAsync(int userId)
    {
        var trades = (await _tradeRepository.GetByUserIdAsync(userId))
            .OrderBy(t => t.TradeDate)
            .ThenBy(t => t.EntryTime)
            .ToList();

        var equityCurve = new List<EquityPointDto>();
        decimal cumulative = 0;
        decimal peak = 0;

        foreach (var group in trades.GroupBy(t => t.TradeDate))
        {
            var dailyPnL = group.Sum(t => t.PnL);
            cumulative += dailyPnL;
            peak = Math.Max(peak, cumulative);
            var drawdown = peak > 0 ? ((peak - cumulative) / peak) * 100 : 0;

            equityCurve.Add(new EquityPointDto
            {
                Date = group.Key,
                DailyPnL = Math.Round(dailyPnL, 2),
                CumulativePnL = Math.Round(cumulative, 2),
                Drawdown = Math.Round(drawdown, 2)
            });
        }

        return equityCurve;
    }

    public async Task<IEnumerable<StrategyPerformanceDto>> GetStrategyPerformanceAsync(int userId)
    {
        var trades = (await _tradeRepository.GetByUserIdAsync(userId)).ToList();

        return trades
            .GroupBy(t => t.Strategy?.Name ?? "Untagged")
            .Select(g =>
            {
                var wins = g.Where(t => t.PnL > 0).ToList();
                var losses = g.Where(t => t.PnL < 0).ToList();
                var grossProfit = wins.Sum(t => t.PnL);
                var grossLoss = Math.Abs(losses.Sum(t => t.PnL));

                return new StrategyPerformanceDto
                {
                    StrategyName = g.Key,
                    TotalTrades = g.Count(),
                    WinRate = Math.Round((decimal)wins.Count / g.Count() * 100, 2),
                    AverageRR = Math.Round(g.Average(t => t.RR), 2),
                    TotalPnL = Math.Round(g.Sum(t => t.PnL), 2),
                    ProfitFactor = grossLoss == 0 ? grossProfit : Math.Round(grossProfit / grossLoss, 2)
                };
            })
            .OrderByDescending(s => s.TotalPnL)
            .ToList();
    }

    public async Task<IEnumerable<TimePerformanceDto>> GetPerformanceByDayAsync(int userId)
    {
        var trades = (await _tradeRepository.GetByUserIdAsync(userId)).ToList();
        return GroupByLabel(trades, t => t.DayOfWeek);
    }

    public async Task<IEnumerable<TimePerformanceDto>> GetPerformanceBySessionAsync(int userId)
    {
        var trades = (await _tradeRepository.GetByUserIdAsync(userId)).ToList();
        return GroupByLabel(trades, t => t.Session);
    }

    // --- Private helpers ---

    private static decimal CalculateMaxDrawdown(List<Trade> trades)
    {
        decimal peak = 0;
        decimal cumulative = 0;
        decimal maxDrawdown = 0;

        foreach (var trade in trades.OrderBy(t => t.TradeDate).ThenBy(t => t.EntryTime))
        {
            cumulative += trade.PnL;
            peak = Math.Max(peak, cumulative);
            var drawdown = peak - cumulative;
            maxDrawdown = Math.Max(maxDrawdown, drawdown);
        }

        return maxDrawdown;
    }

    private static IEnumerable<TimePerformanceDto> GroupByLabel(
        List<Trade> trades, Func<Trade, string> labelSelector)
    {
        return trades
            .GroupBy(labelSelector)
            .Select(g =>
            {
                var wins = g.Count(t => t.PnL > 0);
                return new TimePerformanceDto
                {
                    Label = g.Key,
                    TotalTrades = g.Count(),
                    WinRate = Math.Round((decimal)wins / g.Count() * 100, 2),
                    TotalPnL = Math.Round(g.Sum(t => t.PnL), 2),
                    AverageRR = Math.Round(g.Average(t => t.RR), 2)
                };
            })
            .OrderByDescending(t => t.TotalPnL)
            .ToList();
    }
}
