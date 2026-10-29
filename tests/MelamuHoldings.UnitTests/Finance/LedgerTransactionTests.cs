using System.Reflection;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

public class LedgerTransactionTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private static readonly FinancialAccount OperatingZar = Account(10, "Operating", Currency.ZAR);
    private static readonly FinancialAccount ReserveZar = Account(11, "Reserve", Currency.ZAR);
    private static readonly FinancialAccount PayoutsUsd = Account(12, "Payouts", Currency.USD);
    private static readonly Fund Unallocated = WithId(new Fund("Unallocated", null, Now), 1);
    private static readonly Fund Continuity = WithId(new Fund("Business Continuity", null, Now), 3);

    private static TransactionDetails Details(string description = "Test", DateOnly? date = null) => new(date ?? Today, description);
    private static RecordingInfo Recording() => new(1, Now, Guid.NewGuid().ToString("N"), "hash");
    private static Money Zar(decimal amount) => Money.Of(amount, Currency.ZAR);
    private static Money Usd(decimal amount) => Money.Of(amount, Currency.USD);

    [Fact]
    public void Income_posts_a_positive_entry_and_expense_a_negative_one()
    {
        var income = LedgerTransaction.Income(OperatingZar, Unallocated, Zar(1350), Details(), Recording());
        var expense = LedgerTransaction.Expense(OperatingZar, Continuity, Zar(200), Details(), Recording());

        var credit = Assert.Single(income.Entries);
        Assert.Equal((10, 1, Zar(1350)), (credit.FinancialAccountId, credit.FundId, credit.Money));
        Assert.Equal(Zar(-200), Assert.Single(expense.Entries).Money);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Income_and_expense_require_a_positive_amount(decimal amount)
    {
        Assert.Throws<DomainException>(() => LedgerTransaction.Income(OperatingZar, Unallocated, Zar(amount), Details(), Recording()));
        Assert.Throws<DomainException>(() => LedgerTransaction.Expense(OperatingZar, Unallocated, Zar(amount), Details(), Recording()));
    }

    [Fact]
    public void Opening_balance_may_be_negative_but_not_zero()
    {
        var overdrawn = LedgerTransaction.OpeningBalance(OperatingZar, Unallocated, Zar(-150), Details(), Recording());

        Assert.Equal(Zar(-150), Assert.Single(overdrawn.Entries).Money);
        Assert.Throws<DomainException>(() => LedgerTransaction.OpeningBalance(OperatingZar, Unallocated, Zar(0), Details(), Recording()));
    }

    [Fact]
    public void Amount_must_be_in_the_account_currency()
    {
        Assert.Throws<CurrencyMismatchException>(() =>
            LedgerTransaction.Income(OperatingZar, Unallocated, Usd(100), Details(), Recording()));
    }

    [Fact]
    public void Closed_accounts_accept_no_new_entries()
    {
        var closed = Account(20, "Old account", Currency.ZAR);
        closed.Close(Zar(0), Now);

        var error = Assert.Throws<DomainException>(() =>
            LedgerTransaction.Income(closed, Unallocated, Zar(10), Details(), Recording()));
        Assert.Contains("closed", error.Message);
    }

    [Fact]
    public void Transfer_moves_money_between_accounts_and_sums_to_zero()
    {
        var transfer = LedgerTransaction.Transfer(OperatingZar, Unallocated, ReserveZar, Continuity, Zar(405), Details(), Recording());

        Assert.Equal(2, transfer.Entries.Count);
        Assert.Equal(Zar(0), Money.Sum(transfer.Entries.Select(e => e.Money), Currency.ZAR));
        Assert.Contains(transfer.Entries, e => e.FinancialAccountId == 11 && e.FundId == 3 && e.Amount == 405);
    }

    [Fact]
    public void Transfer_rejects_one_account_and_mixed_currencies()
    {
        Assert.Throws<DomainException>(() =>
            LedgerTransaction.Transfer(OperatingZar, Unallocated, OperatingZar, Continuity, Zar(1), Details(), Recording()));

        var error = Assert.Throws<DomainException>(() =>
            LedgerTransaction.Transfer(PayoutsUsd, Unallocated, OperatingZar, Unallocated, Usd(1), Details(), Recording()));
        Assert.Contains("currency exchange", error.Message);
    }

    [Fact]
    public void Reallocation_earmarks_within_one_account_without_moving_money()
    {
        var earmark = LedgerTransaction.FundReallocation(OperatingZar, Unallocated, Continuity, Zar(540), Details(), Recording());

        Assert.All(earmark.Entries, e => Assert.Equal(10, e.FinancialAccountId));
        Assert.Equal(Zar(0), Money.Sum(earmark.Entries.Select(e => e.Money), Currency.ZAR));
        Assert.Throws<DomainException>(() =>
            LedgerTransaction.FundReallocation(OperatingZar, Continuity, Continuity, Zar(1), Details(), Recording()));
    }

    [Fact]
    public void Currency_exchange_records_both_sides_and_the_rate()
    {
        var exchange = LedgerTransaction.CurrencyExchange(
            PayoutsUsd, Unallocated, Usd(1000), OperatingZar, Unallocated, Zar(18250.50m), Details(), Recording());

        Assert.Equal(18.2505m, exchange.ExchangeRate);
        Assert.Contains(exchange.Entries, e => e.Money == Usd(-1000) && e.FinancialAccountId == 12);
        Assert.Contains(exchange.Entries, e => e.Money == Zar(18250.50m) && e.FinancialAccountId == 10);
    }

    [Fact]
    public void Currency_exchange_needs_two_currencies()
    {
        Assert.Throws<DomainException>(() => LedgerTransaction.CurrencyExchange(
            OperatingZar, Unallocated, Zar(10), ReserveZar, Unallocated, Zar(10), Details(), Recording()));
    }

    [Fact]
    public void Reversal_negates_every_entry_and_points_at_the_original()
    {
        var original = WithId(LedgerTransaction.Transfer(OperatingZar, Unallocated, ReserveZar, Continuity, Zar(405), Details(), Recording()), 7);

        var reversal = original.Reverse(Details("Wrong account"), Recording());

        Assert.Equal(LedgerTransactionType.Reversal, reversal.Type);
        Assert.Equal(7, reversal.ReversesTransactionId);
        Assert.Equal(
            original.Entries.Select(e => (e.FinancialAccountId, e.FundId, -e.Amount)),
            reversal.Entries.Select(e => (e.FinancialAccountId, e.FundId, e.Amount)));
    }

    [Fact]
    public void Reversals_cannot_be_reversed_or_dated_before_the_original()
    {
        var original = WithId(LedgerTransaction.Income(OperatingZar, Unallocated, Zar(10), Details(), Recording()), 8);

        Assert.Throws<DomainException>(() => original.Reverse(Details(date: Today.AddDays(-1)), Recording()));
        var reversal = original.Reverse(Details(), Recording());
        Assert.Throws<DomainException>(() => reversal.Reverse(Details(), Recording()));
    }

    [Fact]
    public void Dates_must_be_set_and_not_in_the_future()
    {
        Assert.Throws<DomainException>(() => LedgerTransaction.Income(OperatingZar, Unallocated, Zar(1), Details(date: default(DateOnly)), Recording()));
        Assert.Throws<DomainException>(() => LedgerTransaction.Income(OperatingZar, Unallocated, Zar(1), Details(date: Today.AddDays(2)), Recording()));

        // One day of slack for time zones
        LedgerTransaction.Income(OperatingZar, Unallocated, Zar(1), Details(date: Today.AddDays(1)), Recording());
    }

    [Fact]
    public void Description_is_required_and_bounded()
    {
        Assert.Throws<DomainException>(() => LedgerTransaction.Income(OperatingZar, Unallocated, Zar(1), Details("  "), Recording()));
        Assert.Throws<DomainException>(() => LedgerTransaction.Income(OperatingZar, Unallocated, Zar(1), Details(new string('x', 201)), Recording()));
    }

    [Fact]
    public void Accounts_close_only_at_a_zero_balance_and_only_once()
    {
        var account = Account(30, "Closing", Currency.ZAR);

        Assert.Throws<DomainException>(() => account.Close(Zar(0.01m), Now));
        account.Close(Zar(0), Now);
        Assert.True(account.IsClosed);
        Assert.Throws<DomainException>(() => account.Close(Zar(0), Now));
    }

    [Fact]
    public void The_system_fund_cannot_be_renamed_but_its_description_can_change()
    {
        var unallocated = new Fund("Unallocated", null, Now);
        typeof(Fund).GetProperty(nameof(Fund.IsSystem))!.SetValue(unallocated, true);

        Assert.Throws<DomainException>(() => unallocated.Update("Spare cash", null));
        unallocated.Update("Unallocated", "Received but not yet earmarked");
        Assert.Equal("Received but not yet earmarked", unallocated.Description);
    }

    // Accounts, funds and transactions normally get their ids from the database
    private static FinancialAccount Account(int id, string name, Currency currency) =>
        WithId(new FinancialAccount(name, FinancialAccountType.Bank, currency, Ownership.Company, null, null, Now), id);

    private static T WithId<T>(T entity, int id)
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
        return entity;
    }
}
