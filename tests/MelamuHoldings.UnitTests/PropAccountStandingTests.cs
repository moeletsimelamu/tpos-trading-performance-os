using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.UnitTests;

// Where an account stands against its rules (decision D12), every figure worked out by hand
public class PropAccountStandingTests
{
    private const decimal Start = 100_000m;
    private static readonly DateOnly Run = new(2026, 10, 1);
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static PropAccountRules Rules(
        DrawdownModel model = DrawdownModel.Static, decimal? maxDrawdown = 10_000m, bool locks = false, decimal? dailyLoss = null, decimal? target = null, int? days = null) =>
        new(model, maxDrawdown, locks, dailyLoss, target, days, null, null, null, null, null);

    private static ClosedTradeResult Trade(int dayOffset, decimal pnL, int id = 0, string? at = null) =>
        new(id, Run.AddDays(dayOffset), at is null ? null : TimeOnly.Parse(at), pnL);

    private static BalanceReading Reading(int dayOffset, decimal balance, decimal? level = null) => new(Run.AddDays(dayOffset), balance, level);

    private static PropAccountStanding Calculate(PropAccountRules rules, ClosedTradeResult[] trades, BalanceReading[]? readings = null, DateOnly? today = null) =>
        PropAccountStanding.Calculate(rules, Start, Run, trades, readings ?? [], today ?? Today);

    [Fact]
    public void With_no_trades_the_account_stands_at_its_starting_balance()
    {
        var s = Calculate(Rules(dailyLoss: 5_000m, target: 8_000m, days: 4), []);

        Assert.Equal((Start, 0m, 0, 0), (s.Balance, s.Profit, s.TradesCounted, s.TradingDays));
        Assert.Equal((90_000m, 10_000m, RuleStanding.Clear), (s.Drawdown!.Level, s.Drawdown.Room, s.Drawdown.Standing));
        Assert.Equal((RuleStanding.Clear, (DateOnly?)null, (decimal?)null), (s.DailyLoss!.Standing, s.DailyLoss.LatestDay, s.DailyLoss.WorstLoss));
        Assert.Equal((8_000m, 0m, 8_000m, false), (s.Target!.Target, s.Target.Profit, s.Target.Remaining, s.Target.Reached));
        Assert.Equal((4, 0, 4, false), (s.MinimumDays!.Required, s.MinimumDays.Done, s.MinimumDays.Remaining, s.MinimumDays.Reached));
        Assert.False(s.TargetMet);
    }

    [Fact]
    public void A_rule_the_firm_does_not_set_has_no_figure()
    {
        var s = Calculate(Rules(DrawdownModel.None, null), [Trade(0, 100m)]);

        Assert.Null(s.Drawdown);
        Assert.Null(s.DailyLoss);
        Assert.Null(s.Target);
        Assert.Null(s.MinimumDays);
        Assert.Equal(100_100m, s.Balance);
    }

    [Fact]
    public void Only_trades_closed_in_this_run_count()
    {
        var s = Calculate(Rules(), [Trade(-1, 9_000m), Trade(0, 100m)]);

        Assert.Equal((100_100m, 1, 1), (s.Balance, s.TradesCounted, s.TradingDays));
    }

    [Fact]
    public void A_static_level_stays_where_it_started()
    {
        var s = Calculate(Rules(), [Trade(0, 3_000m), Trade(1, -1_000m)]);

        Assert.Equal((102_000m, 2_000m), (s.Balance, s.Profit));
        Assert.Equal((90_000m, 12_000m), (s.Drawdown!.Level, s.Drawdown.Room));
    }

    [Fact]
    public void A_trailing_level_follows_new_highs_and_never_falls()
    {
        var s = Calculate(Rules(DrawdownModel.TrailingEndOfDay), [Trade(0, 5_000m), Trade(1, -2_000m)]);

        Assert.Equal(103_000m, s.Balance);
        Assert.Equal((95_000m, 8_000m), (s.Drawdown!.Level, s.Drawdown.Room));
    }

    [Fact]
    public void End_of_day_trailing_sees_the_days_close_and_intraday_sees_each_trade()
    {
        ClosedTradeResult[] day = [Trade(0, -4_000m, 2, "11:00"), Trade(0, 5_000m, 1, "09:00")];

        var endOfDay = Calculate(Rules(DrawdownModel.TrailingEndOfDay), day);
        Assert.Equal((101_000m, 91_000m), (endOfDay.Balance, endOfDay.Drawdown!.Level));

        // Taken in the order they closed: up to 105,000 first, then back to 101,000
        var intraday = Calculate(Rules(DrawdownModel.TrailingIntraday), day);
        Assert.Equal((101_000m, 95_000m, 6_000m), (intraday.Balance, intraday.Drawdown!.Level, intraday.Drawdown.Room));
    }

    [Theory]
    [InlineData(true, 100_000, 15_000)]
    [InlineData(false, 105_000, 10_000)]
    public void A_lock_stops_the_level_at_the_starting_balance(bool locks, int level, int room)
    {
        var s = Calculate(Rules(DrawdownModel.TrailingEndOfDay, locks: locks), [Trade(0, 15_000m)]);

        Assert.Equal(((decimal)level, (decimal)room), (s.Drawdown!.Level, s.Drawdown.Room));
    }

    [Theory]
    [InlineData(-7_999.99, RuleStanding.Clear)]
    [InlineData(-8_000, RuleStanding.Near)]
    [InlineData(-9_999.99, RuleStanding.Near)]
    [InlineData(-10_000, RuleStanding.Reached)]
    [InlineData(-12_000, RuleStanding.Reached)]
    public void Near_starts_at_80_percent_of_the_limit_and_reached_at_all_of_it(double pnL, RuleStanding standing)
    {
        var s = Calculate(Rules(), [Trade(0, (decimal)pnL)]);

        Assert.Equal(standing, s.Drawdown!.Standing);
    }

    [Fact]
    public void A_day_is_its_trades_together_and_a_day_at_the_limit_is_kept_even_after_recovering()
    {
        var s = Calculate(Rules(dailyLoss: 5_000m), [Trade(0, -3_000m), Trade(0, -2_000m), Trade(1, 8_000m)]);

        Assert.Equal(RuleStanding.Reached, s.DailyLoss!.Standing);
        Assert.Equal([Run], s.DailyLoss.DaysPastLimit);
        Assert.Equal((Run, 5_000m), (s.DailyLoss.WorstDay, s.DailyLoss.WorstLoss));
        Assert.Equal((Run.AddDays(1), 8_000m), (s.DailyLoss.LatestDay, s.DailyLoss.LatestDayPnL));
    }

    [Fact]
    public void Only_the_current_day_can_be_near_its_limit()
    {
        var limit = Rules(dailyLoss: 5_000m);

        Assert.Equal(RuleStanding.Clear, Calculate(limit, [Trade(0, -4_000m)], today: Run.AddDays(1)).DailyLoss!.Standing);
        Assert.Equal(RuleStanding.Near, Calculate(limit, [Trade(0, -4_000m)], today: Run).DailyLoss!.Standing);
        // The trader's date can be ahead of the server's UTC date
        Assert.Equal(RuleStanding.Near, Calculate(limit, [Trade(1, -4_000m)], today: Run).DailyLoss!.Standing);
        Assert.Equal(RuleStanding.Clear, Calculate(limit, [Trade(0, -3_999.99m)], today: Run).DailyLoss!.Standing);
    }

    [Fact]
    public void A_gaining_day_is_not_a_worst_day()
    {
        var s = Calculate(Rules(dailyLoss: 5_000m), [Trade(0, 100m)]);

        Assert.Equal(((DateOnly?)null, (decimal?)null, RuleStanding.Clear), (s.DailyLoss!.WorstDay, s.DailyLoss.WorstLoss, s.DailyLoss.Standing));
    }

    [Fact]
    public void The_target_is_met_once_profit_reaches_it_and_the_minimum_days_are_traded()
    {
        var two = new[] { Trade(0, 4_000m), Trade(1, 4_000m) };
        var four = new[] { Trade(0, 2_000m), Trade(0, 1_000m), Trade(1, 2_000m), Trade(2, 2_000m), Trade(3, 1_000m) };

        var shortOfDays = Calculate(Rules(target: 8_000m, days: 4), two);
        Assert.True(shortOfDays.Target!.Reached);
        Assert.Equal((2, 2, false), (shortOfDays.MinimumDays!.Done, shortOfDays.MinimumDays.Remaining, shortOfDays.MinimumDays.Reached));
        Assert.False(shortOfDays.TargetMet);

        var enough = Calculate(Rules(target: 8_000m, days: 4), four);
        Assert.Equal((4, 5), (enough.TradingDays, enough.TradesCounted));
        Assert.True(enough.TargetMet);

        Assert.True(Calculate(Rules(target: 8_000m), two).TargetMet);
        Assert.False(Calculate(Rules(target: 8_000.01m), two).TargetMet);
        Assert.False(Calculate(Rules(days: 1), two).TargetMet);
        Assert.Equal(0.01m, Calculate(Rules(target: 8_000.01m), two).Target!.Remaining);
        Assert.Equal(0m, Calculate(Rules(target: 7_000m), two).Target!.Remaining);
    }

    [Fact]
    public void A_reading_replaces_the_balance_and_later_trades_move_on_from_it()
    {
        var s = Calculate(Rules(), [Trade(0, 1_000m), Trade(1, 300m), Trade(2, 200m)], [Reading(1, 100_500m)]);

        // 101,000 after day 0 and 101,300 after day 1's trade, which the reading includes; the firm says 100,500
        var check = Assert.Single(s.Readings);
        Assert.Equal((Run.AddDays(1), 100_500m, 101_300m, -800m), (check.AsOf, check.Balance, check.Expected, check.Difference));
        Assert.Equal((100_700m, 700m, 3), (s.Balance, s.Profit, s.TradesCounted));
    }

    [Fact]
    public void A_reading_on_a_day_without_trades_is_not_a_trading_day_and_one_before_the_run_is_ignored()
    {
        var s = Calculate(Rules(days: 2), [Trade(0, 100m)], [Reading(-1, 1m), Reading(5, 100_100m)]);

        Assert.Equal((100_100m, 1), (s.Balance, s.TradingDays));
        Assert.Equal(0m, Assert.Single(s.Readings).Difference);
    }

    [Fact]
    public void A_higher_reading_moves_a_trailing_level_up()
    {
        var s = Calculate(Rules(DrawdownModel.TrailingEndOfDay), [], [Reading(0, 104_000m)]);

        Assert.Equal((104_000m, 94_000m), (s.Balance, s.Drawdown!.Level));
    }

    [Fact]
    public void After_a_payout_the_level_the_firm_shows_replaces_the_calculated_one_and_later_highs_move_it_up()
    {
        var rules = Rules(DrawdownModel.TrailingEndOfDay);
        ClosedTradeResult[] trades = [Trade(0, 10_000m)];

        // Without the firm's level, the balance alone looks like a loss: 110,000 down to 101,000
        var alone = Calculate(rules, trades, [Reading(1, 101_000m)]);
        Assert.Equal((100_000m, 1_000m, RuleStanding.Near), (alone.Drawdown!.Level, alone.Drawdown.Room, alone.Drawdown.Standing));

        var stated = Calculate(rules, trades, [Reading(1, 101_000m, 91_000m)]);
        Assert.Equal((91_000m, 10_000m, RuleStanding.Clear), (stated.Drawdown!.Level, stated.Drawdown.Room, stated.Drawdown.Standing));

        var later = Calculate(rules, [.. trades, Trade(2, 2_000m)], [Reading(1, 101_000m, 91_000m)]);
        Assert.Equal((103_000m, 93_000m), (later.Balance, later.Drawdown!.Level));
    }

    [Fact]
    public void A_stated_level_does_not_apply_once_the_rules_have_no_limit()
    {
        var s = Calculate(Rules(DrawdownModel.None, null), [], [Reading(0, 100_000m, 95_000m)]);

        Assert.Null(s.Drawdown);
    }

    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static PropFirmAccount Account(PropAccountRules? rules = null) =>
        new(1, "Firm", "Attempt", Money.Of(Start, Currency.USD), PropAccountPhase.Evaluation, null, Run, rules ?? Rules(), null, Now);

    private static Money Usd(decimal amount) => Money.Of(amount, Currency.USD);

    [Fact]
    public void A_balance_is_recorded_once_per_day_and_kept_in_the_history()
    {
        var a = Account();
        a.RecordBalance(Run.AddDays(3), Usd(101_000m), Usd(91_000m), " After a payout ", Now);

        var entry = Assert.Single(a.Balances);
        Assert.Equal((Run.AddDays(3), 101_000m, 91_000m, "After a payout"), (entry.AsOf, entry.Amount, entry.DrawdownLevel, entry.Note));
        Assert.Equal((PropAccountEventKind.BalanceRecorded, "Balance USD 101000.00, drawdown level USD 91000.00"), (a.Events[^1].Kind, a.Events[^1].Note));

        var ex = Assert.Throws<DomainException>(() => a.RecordBalance(Run.AddDays(3), Usd(1m), null, null, Now));
        Assert.Contains("already recorded", ex.Message);
        // A balance does not restart anything
        Assert.Equal((Run, Start), (a.RunStartedOn, a.StartingBalance));
    }

    [Fact]
    public void A_balance_cannot_be_before_the_run_in_the_future_below_zero_or_in_another_currency()
    {
        var a = Account();
        Assert.Throws<DomainException>(() => a.RecordBalance(Run.AddDays(-1), Usd(1m), null, null, Now));
        Assert.Throws<DomainException>(() => a.RecordBalance(DateOnly.FromDateTime(Now).AddDays(2), Usd(1m), null, null, Now));
        Assert.Throws<DomainException>(() => a.RecordBalance(Run, Usd(-1m), null, null, Now));
        Assert.Throws<DomainException>(() => a.RecordBalance(Run, Usd(1m), Usd(-1m), null, Now));
        Assert.Throws<CurrencyMismatchException>(() => a.RecordBalance(Run, Money.Of(1m, Currency.ZAR), null, null, Now));
        Assert.Throws<CurrencyMismatchException>(() => a.RecordBalance(Run, Usd(1m), Money.Of(1m, Currency.ZAR), null, Now));

        a.RecordBalance(Run, Usd(0m), null, null, Now);
        Assert.Single(a.Balances);
    }

    [Fact]
    public void A_drawdown_level_needs_a_drawdown_limit_and_a_closed_account_takes_no_balance()
    {
        Assert.Throws<DomainException>(() => Account(Rules(DrawdownModel.None, null)).RecordBalance(Run, Usd(1m), Usd(1m), null, Now));

        var closed = Account();
        closed.ChangeStatus(PropAccountStatus.Closed, Run, null, Now);
        Assert.Throws<DomainException>(() => closed.RecordBalance(Run, Usd(1m), null, null, Now));
    }

    [Fact]
    public void A_balance_does_not_block_a_status_dated_before_it()
    {
        var a = Account();
        a.RecordBalance(Run.AddDays(9), Usd(100_000m), null, null, Now);
        a.ChangeStatus(PropAccountStatus.Pending, Run.AddDays(2), null, Now);
        Assert.Equal(PropAccountStatus.Pending, a.Status);
    }

    [Fact]
    public void Trades_link_to_an_account_taking_its_name_and_currency()
    {
        var account = Account();
        var trade = new Trade { TradingAccount = "Typed", Currency = "USD" };
        Assert.Equal("Typed", trade.AccountName);

        trade.LinkTo(account);
        Assert.Equal(("Attempt", null, "USD"), (trade.AccountName, trade.TradingAccount, trade.Currency));
        Assert.Same(account, trade.PropFirmAccount);

        trade.LinkTo(null);
        Assert.Null(trade.PropFirmAccountId);
        Assert.Null(trade.AccountName);
    }

    [Fact]
    public void A_trade_stated_in_another_currency_cannot_link()
    {
        var ex = Assert.Throws<DomainException>(() => new Trade { Currency = "ZAR" }.LinkTo(Account()));
        Assert.Contains("is in USD", ex.Message);

        var noCurrency = new Trade();
        noCurrency.LinkTo(Account());
        Assert.Equal("USD", noCurrency.Currency);
    }
}
