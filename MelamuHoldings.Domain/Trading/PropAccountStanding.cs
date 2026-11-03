namespace MelamuHoldings.Domain.Trading;

// Where an account stands against one rule. Near is 80% of the limit used.
public enum RuleStanding { Clear, Near, Reached }

// A closed trade linked to the account: its realised P&L and when it closed
public sealed record ClosedTradeResult(int TradeId, DateOnly ClosedOn, TimeOnly? ClosedAt, decimal PnL);

// A balance read from the firm's dashboard at the end of a day, with the drawdown level it shows if one was entered
public sealed record BalanceReading(DateOnly AsOf, decimal Balance, decimal? DrawdownLevel);

// The balance below which the firm's drawdown rule is broken, and how far above it the account is
public sealed record DrawdownStanding(DrawdownModel Model, decimal Limit, decimal Level, decimal Room, RuleStanding Standing);

// Losses are measured per closing day from closed trades. A later version of the rules applies to the whole run.
public sealed record DailyLossStanding(
    decimal Limit, DateOnly? LatestDay, decimal? LatestDayPnL, DateOnly? WorstDay, decimal? WorstLoss, IReadOnlyList<DateOnly> DaysPastLimit, RuleStanding Standing);

public sealed record TargetStanding(decimal Target, decimal Profit, decimal Remaining, bool Reached);

public sealed record TradingDaysStanding(int Required, int Done, int Remaining, bool Reached);

// What the recorded trades came to on a reading's date, against what the firm showed
public sealed record ReadingCheck(DateOnly AsOf, decimal Balance, decimal Expected, decimal Difference);

// An account's position in the current run, worked out from its linked closed trades and the balances entered from
// the firm's dashboard. These are the app's figures, not the firm's: the firm's own numbers decide, trades not recorded
// are not in them, and unrealised profit and loss cannot be seen from closed trades. Nothing here changes any record.
public sealed record PropAccountStanding(
    decimal Balance, decimal Profit, int TradesCounted, int TradingDays, DrawdownStanding? Drawdown, DailyLossStanding? DailyLoss,
    TargetStanding? Target, TradingDaysStanding? MinimumDays, IReadOnlyList<ReadingCheck> Readings)
{
    public const decimal NearShare = 0.8m;

    public bool TargetMet => Target is { Reached: true } && MinimumDays is null or { Reached: true };

    // The balance starts at the run's starting balance and moves with each closed trade. A reading replaces it as of the
    // end of its day, because the firm's figure is the better one; trades on later days move on from there.
    public static PropAccountStanding Calculate(
        PropAccountRules rules, decimal startingBalance, DateOnly runStartedOn,
        IEnumerable<ClosedTradeResult> trades, IEnumerable<BalanceReading> readings, DateOnly today)
    {
        var counted = trades.Where(t => t.ClosedOn >= runStartedOn).ToList();
        var byDay = counted.ToLookup(t => t.ClosedOn);
        var readingOn = readings.Where(r => r.AsOf >= runStartedOn).ToDictionary(r => r.AsOf);
        var days = byDay.Select(d => d.Key).Union(readingOn.Keys).Order().ToList();

        var balance = startingBalance;
        decimal? level = rules.MaxDrawdown is { } limit ? startingBalance - limit : null;
        var checks = new List<ReadingCheck>();
        var dayResults = new List<(DateOnly Day, decimal PnL)>();

        foreach (var day in days)
        {
            var dayPnL = 0m;
            foreach (var trade in byDay[day].OrderBy(t => t.ClosedAt).ThenBy(t => t.TradeId))
            {
                dayPnL += trade.PnL;
                balance += trade.PnL;
                // Intraday trailing follows the balance within the day, as far as closed trades can show it
                if (rules.DrawdownModel == DrawdownModel.TrailingIntraday) level = Trailed(level, balance, rules, startingBalance);
            }
            if (byDay.Contains(day)) dayResults.Add((day, dayPnL));

            if (readingOn.TryGetValue(day, out var reading))
            {
                checks.Add(new ReadingCheck(day, reading.Balance, balance, reading.Balance - balance));
                balance = reading.Balance;
            }

            // The level the firm shows is taken as it is; only later highs can move it up
            if (reading?.DrawdownLevel is { } stated) level = rules.MaxDrawdown is null ? null : stated;
            else if (rules.DrawdownModel is DrawdownModel.TrailingEndOfDay or DrawdownModel.TrailingIntraday) level = Trailed(level, balance, rules, startingBalance);
        }

        var profit = balance - startingBalance;
        return new PropAccountStanding(
            balance, profit, counted.Count, byDay.Count,
            MeasureDrawdown(rules, level, balance),
            MeasureDailyLoss(rules, dayResults, today),
            rules.ProfitTarget is { } target ? new TargetStanding(target, profit, Math.Max(target - profit, 0), profit >= target) : null,
            rules.MinimumTradingDays is { } required ? new TradingDaysStanding(required, byDay.Count, Math.Max(required - byDay.Count, 0), byDay.Count >= required) : null,
            checks);
    }

    // The level rises with new highs and never falls. With a lock, it stops at the starting balance.
    private static decimal? Trailed(decimal? level, decimal balance, PropAccountRules rules, decimal startingBalance)
    {
        if (level is not { } current || rules.MaxDrawdown is not { } limit) return level;
        var followed = balance - limit;
        if (rules.TrailingLocksAtStart) followed = Math.Min(followed, startingBalance);
        return Math.Max(current, followed);
    }

    private static DrawdownStanding? MeasureDrawdown(PropAccountRules rules, decimal? level, decimal balance)
    {
        if (rules.MaxDrawdown is not { } limit || level is not { } current) return null;
        var room = balance - current;
        var standing = room <= 0 ? RuleStanding.Reached : room <= limit * (1 - NearShare) ? RuleStanding.Near : RuleStanding.Clear;
        return new DrawdownStanding(rules.DrawdownModel, limit, current, room, standing);
    }

    private static DailyLossStanding? MeasureDailyLoss(PropAccountRules rules, List<(DateOnly Day, decimal PnL)> days, DateOnly today)
    {
        if (rules.DailyLossLimit is not { } limit) return null;
        var past = days.Where(d => -d.PnL >= limit).Select(d => d.Day).ToList();
        var latest = days.Count == 0 ? ((DateOnly Day, decimal PnL)?)null : days[^1];
        var worst = days.Where(d => d.PnL < 0).OrderBy(d => d.PnL).ThenBy(d => d.Day).Select(d => ((DateOnly Day, decimal PnL)?)d).FirstOrDefault();
        // Only the current day can still be saved, so only it is "near". Dates are the trader's and the clock is UTC.
        var near = latest is { } l && l.Day >= today && -l.PnL >= limit * NearShare;
        var standing = past.Count > 0 ? RuleStanding.Reached : near ? RuleStanding.Near : RuleStanding.Clear;
        return new DailyLossStanding(limit, latest?.Day, latest?.PnL, worst?.Day, worst is { } w ? -w.PnL : null, past, standing);
    }
}
