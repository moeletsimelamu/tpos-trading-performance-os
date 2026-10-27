using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public class BehaviourAlertDto
{
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<int> TradeIds { get; set; } = new();
    public DateOnly DetectedOnDate { get; set; }
}

public class BehaviourSummaryDto
{
    public List<BehaviourAlertDto> Alerts { get; set; } = new();
    public int RevengeTradingCount { get; set; }
    public int OvertradingDayCount { get; set; }
    public int MaxConsecutiveLosses { get; set; }
    public int StrategyDeviationCount { get; set; }
    public decimal DeviationPnLImpact { get; set; }
}

public class BehaviourAnalyticsService
{
    private readonly ITradeRepository _tradeRepository;

    // Configurable thresholds
    private const int RevengeTradingWindowMinutes = 20;
    private const int OvertradingDailyLimit = 5;
    private const int ConsecutiveLossThreshold = 3;

    public BehaviourAnalyticsService(ITradeRepository tradeRepository)
    {
        _tradeRepository = tradeRepository;
    }

    public async Task<BehaviourSummaryDto> GetBehaviourSummaryAsync(int userId)
    {
        var trades = (await _tradeRepository.GetByUserIdAsync(userId))
            .OrderBy(t => t.TradeDate)
            .ThenBy(t => t.EntryTime)
            .ToList();

        var alerts = new List<BehaviourAlertDto>();

        alerts.AddRange(DetectRevengeTrading(trades));
        alerts.AddRange(DetectOvertrading(trades));
        alerts.AddRange(DetectConsecutiveLosses(trades));

        var deviations = trades.Where(t => t.IsManualOverride).ToList();

        return new BehaviourSummaryDto
        {
            Alerts = alerts,
            RevengeTradingCount = alerts.Count(a => a.AlertType == "RevengeTrading"),
            OvertradingDayCount = alerts.Count(a => a.AlertType == "Overtrading"),
            MaxConsecutiveLosses = GetMaxConsecutiveLosses(trades),
            StrategyDeviationCount = deviations.Count,
            DeviationPnLImpact = Math.Round(deviations.Sum(t => t.PnL), 2)
        };
    }

    private static List<BehaviourAlertDto> DetectRevengeTrading(List<Trade> trades)
    {
        var alerts = new List<BehaviourAlertDto>();

        for (int i = 1; i < trades.Count; i++)
        {
            var previous = trades[i - 1];
            var current = trades[i];

            if (previous.PnL >= 0) continue;
            if (previous.TradeDate != current.TradeDate) continue;

            var previousDateTime = previous.TradeDate.ToDateTime(previous.EntryTime);
            var currentDateTime = current.TradeDate.ToDateTime(current.EntryTime);
            var minutesBetween = (currentDateTime - previousDateTime).TotalMinutes;

            if (minutesBetween <= RevengeTradingWindowMinutes && minutesBetween >= 0)
            {
                alerts.Add(new BehaviourAlertDto
                {
                    AlertType = "RevengeTrading",
                    Severity = "Warning",
                    Description = $"Trade #{current.Id} was entered {minutesBetween:F0} minutes after a losing trade on {current.TradeDate}.",
                    TradeIds = new List<int> { previous.Id, current.Id },
                    DetectedOnDate = current.TradeDate
                });
            }
        }

        return alerts;
    }

    private static List<BehaviourAlertDto> DetectOvertrading(List<Trade> trades)
    {
        return trades
            .GroupBy(t => t.TradeDate)
            .Where(g => g.Count() > OvertradingDailyLimit)
            .Select(g => new BehaviourAlertDto
            {
                AlertType = "Overtrading",
                Severity = g.Count() > OvertradingDailyLimit * 2 ? "Critical" : "Warning",
                Description = $"{g.Count()} trades placed on {g.Key} (limit: {OvertradingDailyLimit}).",
                TradeIds = g.Select(t => t.Id).ToList(),
                DetectedOnDate = g.Key
            })
            .ToList();
    }

    private static List<BehaviourAlertDto> DetectConsecutiveLosses(List<Trade> trades)
    {
        var alerts = new List<BehaviourAlertDto>();
        var streak = new List<Trade>();

        foreach (var trade in trades)
        {
            if (trade.PnL < 0)
            {
                streak.Add(trade);
            }
            else
            {
                if (streak.Count >= ConsecutiveLossThreshold)
                {
                    alerts.Add(new BehaviourAlertDto
                    {
                        AlertType = "ConsecutiveLosses",
                        Severity = streak.Count >= 5 ? "Critical" : "Warning",
                        Description = $"{streak.Count} consecutive losing trades ending on {streak.Last().TradeDate}.",
                        TradeIds = streak.Select(t => t.Id).ToList(),
                        DetectedOnDate = streak.Last().TradeDate
                    });
                }
                streak.Clear();
            }
        }

        // Catch a streak at the end of the list
        if (streak.Count >= ConsecutiveLossThreshold)
        {
            alerts.Add(new BehaviourAlertDto
            {
                AlertType = "ConsecutiveLosses",
                Severity = streak.Count >= 5 ? "Critical" : "Warning",
                Description = $"{streak.Count} consecutive losing trades ending on {streak.Last().TradeDate}.",
                TradeIds = streak.Select(t => t.Id).ToList(),
                DetectedOnDate = streak.Last().TradeDate
            });
        }

        return alerts;
    }

    private static int GetMaxConsecutiveLosses(List<Trade> trades)
    {
        int max = 0, current = 0;
        foreach (var trade in trades)
        {
            if (trade.PnL < 0) { current++; max = Math.Max(max, current); }
            else current = 0;
        }
        return max;
    }
}
