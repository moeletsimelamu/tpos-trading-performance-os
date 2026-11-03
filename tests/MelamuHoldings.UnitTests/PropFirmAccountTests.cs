using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.UnitTests;

// One record per attempt (decision D10): phases, resets and rule versions stay on it, and every change is kept
public class PropFirmAccountTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 10, 1);

    private static PropAccountRules Rules(
        DrawdownModel model = DrawdownModel.Static, decimal? maxDrawdown = 10_000m, bool locks = false, decimal? dailyLoss = 5_000m, decimal? target = 10_000m,
        int? days = 4, decimal? split = 80m, string? notes = null) =>
        new(model, maxDrawdown, locks, dailyLoss, target, days, split, 500m, 400m, null, notes);

    private static PropFirmAccount Account(PropAccountPhase phase = PropAccountPhase.Evaluation, PropAccountRules? rules = null) =>
        new(1, " Test Firm ", " 100k attempt 1 ", Money.Of(100_000m, Currency.USD), phase, " L-1 ", Day, rules ?? Rules(), null, Now);

    [Fact]
    public void A_new_account_starts_active_with_its_balance_rules_and_first_history_entry()
    {
        var a = Account();

        Assert.Equal(("Test Firm", "100k attempt 1", "L-1"), (a.Firm, a.Nickname, a.Login));
        Assert.Equal((PropAccountStatus.Active, PropAccountPhase.Evaluation, Day, 100_000m), (a.Status, a.Phase, a.RunStartedOn, a.StartingBalance));
        Assert.Equal((1, Day, 10_000m), (a.CurrentRules.Version, a.CurrentRules.EffectiveFrom, a.CurrentRules.MaxDrawdown));
        var opened = Assert.Single(a.Events);
        Assert.Equal((PropAccountEventKind.Opened, Day, 100_000m), (opened.Kind, opened.OccurredOn, opened.StartingBalance));
    }

    public static TheoryData<decimal> BadSizes => new() { 0m, -1m };

    [Theory]
    [MemberData(nameof(BadSizes))]
    public void An_account_size_must_be_positive(decimal size) =>
        Assert.Throws<DomainException>(() => new PropFirmAccount(1, "F", "N", Money.Of(size, Currency.USD), PropAccountPhase.Funded, null, Day, Rules(), null, Now));

    [Fact]
    public void An_opening_date_in_the_future_is_refused_but_tomorrow_is_allowed_because_now_is_utc()
    {
        var today = DateOnly.FromDateTime(Now);
        new PropFirmAccount(1, "F", "N", Money.Of(100_000m, Currency.USD), PropAccountPhase.Funded, null, today.AddDays(1), Rules(), null, Now);
        Assert.Throws<DomainException>(() => new PropFirmAccount(1, "F", "N", Money.Of(100_000m, Currency.USD), PropAccountPhase.Funded, null, today.AddDays(2), Rules(), null, Now));
    }

    public static TheoryData<PropAccountRules, string> BadRules => new()
    {
        { Rules(DrawdownModel.None), "Choose how the firm measures" },
        { Rules(DrawdownModel.Static, null), "Give the drawdown limit" },
        { Rules((DrawdownModel)99), "Choose a drawdown model" },
        { Rules(maxDrawdown: 0m), "more than zero" },
        { Rules(maxDrawdown: 100_000m), "less than the starting balance" },
        { Rules(dailyLoss: 100_000m), "daily loss limit must be less" },
        { Rules(dailyLoss: -1m), "more than zero" },
        { Rules(target: 10.005m), "decimal places" },
        { Rules(days: 0), "minimum trading days" },
        { Rules(days: 1001), "minimum trading days" },
        { Rules(split: 0m), "payout split" },
        { Rules(split: 100.01m), "payout split" },
        { Rules(split: 80.125m), "two decimal places" }
    };

    [Theory]
    [MemberData(nameof(BadRules))]
    public void Rules_that_cannot_apply_are_refused_with_the_reason(PropAccountRules rules, string message)
    {
        var ex = Assert.Throws<DomainException>(() => Account(rules: rules));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void Every_limit_is_optional_and_a_lock_only_applies_to_a_trailing_model()
    {
        var bare = Account(rules: new PropAccountRules(DrawdownModel.None, null, true, null, null, null, null, null, null, null, "  "));
        Assert.False(bare.CurrentRules.TrailingLocksAtStart);
        Assert.Null(bare.CurrentRules.Notes);

        var trailing = Account(rules: Rules(DrawdownModel.TrailingEndOfDay, locks: true));
        Assert.True(trailing.CurrentRules.TrailingLocksAtStart);
    }

    [Fact]
    public void Passing_a_phase_stays_on_the_record_restarts_the_balance_and_adds_that_phases_rules()
    {
        var a = Account();
        var on = Day.AddDays(20);
        a.ChangeStatus(PropAccountStatus.Pending, on.AddDays(-1), "Waiting for the next login", Now);

        a.MoveToPhase(PropAccountPhase.Funded, on, Money.Of(100_000m, Currency.USD), "L-2", Rules(target: null), null, Now);

        Assert.Equal((PropAccountPhase.Funded, PropAccountStatus.Active, on, "L-2"), (a.Phase, a.Status, a.RunStartedOn, a.Login));
        Assert.Equal((2, PropAccountPhase.Funded, on, (decimal?)null), (a.CurrentRules.Version, a.CurrentRules.Phase, a.CurrentRules.EffectiveFrom, a.CurrentRules.ProfitTarget));
        Assert.Equal(10_000m, a.RuleSets[0].ProfitTarget);
        var moved = a.Events[^1];
        Assert.Equal((PropAccountEventKind.PhaseChanged, on, PropAccountPhase.Funded, "L-2", 100_000m), (moved.Kind, moved.OccurredOn, moved.Phase, moved.Login, moved.StartingBalance));
        // What it was before stays in the history
        Assert.Equal(("L-1", PropAccountPhase.Evaluation), (a.Events[0].Login, a.Events[0].Phase));
    }

    [Fact]
    public void The_login_stays_and_the_size_is_the_starting_balance_unless_others_are_given()
    {
        var a = Account();
        a.MoveToPhase(PropAccountPhase.Verification, Day.AddDays(5), null, null, Rules(), null, Now);
        Assert.Equal(("L-1", 100_000m), (a.Login, a.StartingBalance));

        a.MoveToPhase(PropAccountPhase.Funded, Day.AddDays(9), Money.Of(50_000m, Currency.USD), null, Rules(dailyLoss: 2_500m, maxDrawdown: 5_000m), null, Now);
        Assert.Equal(50_000m, a.StartingBalance);
    }

    [Fact]
    public void Limits_are_checked_against_the_new_starting_balance()
    {
        var a = Account();
        var ex = Assert.Throws<DomainException>(() => a.MoveToPhase(PropAccountPhase.Funded, Day.AddDays(5), Money.Of(9_000m, Currency.USD), null, Rules(), null, Now));
        Assert.Contains("less than the starting balance", ex.Message);
    }

    [Fact]
    public void Going_back_a_phase_is_a_correction_and_needs_a_reason()
    {
        var a = Account();
        a.MoveToPhase(PropAccountPhase.Funded, Day.AddDays(5), null, null, Rules(), null, Now);

        Assert.Throws<DomainException>(() => a.MoveToPhase(PropAccountPhase.Evaluation, Day.AddDays(5), null, null, Rules(), "  ", Now));
        a.MoveToPhase(PropAccountPhase.Evaluation, Day.AddDays(5), null, null, Rules(), "Moved by mistake", Now);

        Assert.Equal(PropAccountPhase.Evaluation, a.Phase);
        Assert.Equal("Moved by mistake", a.Events[^1].Note);
    }

    [Fact]
    public void The_same_phase_a_breached_account_a_closed_account_and_an_early_date_cannot_change_phase()
    {
        Assert.Throws<DomainException>(() => Account().MoveToPhase(PropAccountPhase.Evaluation, Day.AddDays(1), null, null, Rules(), null, Now));
        Assert.Throws<DomainException>(() => Account().MoveToPhase(PropAccountPhase.Funded, Day.AddDays(-1), null, null, Rules(), null, Now));

        var breached = Account();
        breached.ChangeStatus(PropAccountStatus.Breached, Day.AddDays(2), "Daily loss", Now);
        Assert.Throws<DomainException>(() => breached.MoveToPhase(PropAccountPhase.Funded, Day.AddDays(3), null, null, Rules(), null, Now));

        var closed = Account();
        closed.ChangeStatus(PropAccountStatus.Closed, Day.AddDays(2), null, Now);
        Assert.Throws<DomainException>(() => closed.MoveToPhase(PropAccountPhase.Funded, Day.AddDays(3), null, null, Rules(), null, Now));
    }

    [Fact]
    public void A_breach_says_which_rule_and_leaving_a_breach_or_a_close_needs_a_reason()
    {
        var a = Account();
        Assert.Throws<DomainException>(() => a.ChangeStatus(PropAccountStatus.Breached, Day.AddDays(1), " ", Now));
        a.ChangeStatus(PropAccountStatus.Breached, Day.AddDays(1), "Daily loss limit", Now);

        Assert.Throws<DomainException>(() => a.ChangeStatus(PropAccountStatus.Active, Day.AddDays(2), null, Now));
        a.ChangeStatus(PropAccountStatus.Active, Day.AddDays(2), "The firm reversed it", Now);
        Assert.Equal(PropAccountStatus.Active, a.Status);

        a.ChangeStatus(PropAccountStatus.Closed, Day.AddDays(3), null, Now);
        Assert.Throws<DomainException>(() => a.ChangeStatus(PropAccountStatus.Active, Day.AddDays(4), null, Now));

        Assert.Equal(
            [PropAccountEventKind.Opened, PropAccountEventKind.StatusChanged, PropAccountEventKind.StatusChanged, PropAccountEventKind.StatusChanged],
            a.Events.Select(e => e.Kind));
    }

    [Fact]
    public void A_status_cannot_repeat_or_be_dated_before_the_last_change()
    {
        var a = Account();
        Assert.Throws<DomainException>(() => a.ChangeStatus(PropAccountStatus.Active, Day.AddDays(1), null, Now));
        a.ChangeStatus(PropAccountStatus.Pending, Day.AddDays(5), null, Now);
        Assert.Throws<DomainException>(() => a.ChangeStatus(PropAccountStatus.Active, Day.AddDays(4), null, Now));
    }

    [Fact]
    public void A_reset_makes_a_breached_account_active_in_the_same_phase_and_restarts_the_balance()
    {
        var a = Account();
        a.ChangeStatus(PropAccountStatus.Breached, Day.AddDays(5), "Max drawdown", Now);

        a.Reset(Day.AddDays(6), null, "Paid reset", Now);

        Assert.Equal((PropAccountStatus.Active, PropAccountPhase.Evaluation, Day.AddDays(6), 100_000m), (a.Status, a.Phase, a.RunStartedOn, a.StartingBalance));
        Assert.Equal((PropAccountEventKind.Reset, "Paid reset", 100_000m), (a.Events[^1].Kind, a.Events[^1].Note, a.Events[^1].StartingBalance));
        Assert.Equal(1, a.CurrentRules.Version);
    }

    [Fact]
    public void A_closed_account_cannot_be_reset_or_edited_until_it_is_reopened()
    {
        var a = Account();
        a.ChangeStatus(PropAccountStatus.Closed, Day.AddDays(5), null, Now);

        Assert.Throws<DomainException>(() => a.Reset(Day.AddDays(6), null, null, Now));
        Assert.Throws<DomainException>(() => a.ChangeRules(Rules(days: 9), Day.AddDays(6), Now));
        Assert.Throws<DomainException>(() => a.UpdateDetails("F", "N", null, Money.Of(100_000m, Currency.USD), null, Now));

        a.ChangeStatus(PropAccountStatus.Active, Day.AddDays(6), "Closed by mistake", Now);
        a.ChangeRules(Rules(days: 9), Day.AddDays(6), Now);
        Assert.Equal(2, a.CurrentRules.Version);
    }

    [Fact]
    public void Changed_rules_add_a_version_and_keep_the_old_one_while_unchanged_rules_add_nothing()
    {
        var a = Account();

        a.ChangeRules(Rules(), Day.AddDays(1), Now);
        Assert.Single(a.RuleSets);
        Assert.Single(a.Events);

        a.ChangeRules(Rules(dailyLoss: 3_000m), Day.AddDays(2), Now);
        Assert.Equal([1, 2], a.RuleSets.Select(r => r.Version));
        Assert.Equal((5_000m, 3_000m), (a.RuleSets[0].DailyLossLimit, a.CurrentRules.DailyLossLimit));
        Assert.Equal((PropAccountEventKind.RulesChanged, Day.AddDays(2), "Rules version 2"), (a.Events[^1].Kind, a.Events[^1].OccurredOn, a.Events[^1].Note));
    }

    [Fact]
    public void Rules_cannot_apply_before_the_current_phase_or_the_current_rules_and_later_changes_keep_dates_in_order()
    {
        var a = Account();
        a.MoveToPhase(PropAccountPhase.Funded, Day.AddDays(10), null, null, Rules(), null, Now);
        Assert.Throws<DomainException>(() => a.ChangeRules(Rules(days: 9), Day.AddDays(9), Now));

        a.ChangeRules(Rules(days: 9), Day.AddDays(15), Now);
        Assert.Throws<DomainException>(() => a.ChangeRules(Rules(days: 8), Day.AddDays(14), Now));
        // A later phase or reset cannot start before rules already in force, or versions would go back in time
        Assert.Throws<DomainException>(() => a.Reset(Day.AddDays(14), null, null, Now));
    }

    [Fact]
    public void Correcting_details_records_what_changed_and_nothing_when_nothing_did()
    {
        var a = Account();
        a.UpdateDetails("Test Firm", "100k attempt 1", "L-1", Money.Of(100_000m, Currency.USD), null, Now);
        Assert.Single(a.Events);

        a.UpdateDetails("Better Firm", "100k attempt 1", null, Money.Of(100_000m, Currency.USD), "A note", Now);

        Assert.Equal(("Better Firm", null, "A note"), (a.Firm, a.Login, a.Notes));
        var e = a.Events[^1];
        Assert.Equal(PropAccountEventKind.DetailsChanged, e.Kind);
        Assert.Equal("Firm: Test Firm to Better Firm; Login: L-1 to none; Notes changed", e.Note);
        Assert.Null(e.StartingBalance);
        Assert.Equal(100_000m, a.StartingBalance);
    }

    [Fact]
    public void Correcting_the_size_corrects_the_starting_balance_and_is_checked_against_the_rules()
    {
        var a = Account();
        a.UpdateDetails("Test Firm", "100k attempt 1", "L-1", Money.Of(50_000m, Currency.USD), null, Now);
        Assert.Equal((50_000m, 50_000m), (a.AccountSize, a.StartingBalance));
        Assert.Equal(("Size: USD 100000.00 to USD 50000.00", 50_000m), (a.Events[^1].Note, a.Events[^1].StartingBalance));

        Assert.Throws<DomainException>(() => a.UpdateDetails("Test Firm", "100k attempt 1", "L-1", Money.Of(10_000m, Currency.USD), null, Now));
    }

    [Fact]
    public void The_currency_is_fixed_and_a_details_correction_does_not_block_backdating_a_status()
    {
        var a = Account();
        Assert.Throws<DomainException>(() => a.UpdateDetails("F", "N", null, Money.Of(100_000m, Currency.ZAR), null, Now));

        a.UpdateDetails("Other Firm", "100k attempt 1", null, Money.Of(100_000m, Currency.USD), null, Now);
        a.ChangeStatus(PropAccountStatus.Pending, Day.AddDays(1), null, Now);
        Assert.Equal(PropAccountStatus.Pending, a.Status);
    }

    [Fact]
    public void A_starting_balance_must_be_positive_and_in_the_accounts_currency()
    {
        var a = Account();
        Assert.Throws<CurrencyMismatchException>(() => a.Reset(Day.AddDays(1), Money.Of(1_000m, Currency.ZAR), null, Now));
        Assert.Throws<DomainException>(() => a.Reset(Day.AddDays(1), Money.Of(0m, Currency.USD), null, Now));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(0)]
    public void Long_or_empty_required_text_is_refused(int firmLength) =>
        Assert.Throws<DomainException>(() => new PropFirmAccount(1, new string('x', firmLength), "N", Money.Of(100_000m, Currency.USD), PropAccountPhase.Funded, null, Day, Rules(), null, Now));
}
