using System.Reflection;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

// Phase 7.1: obligations (due dates without drift, payments as links to expenses) and fund minimums and targets
public class ObligationTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FinancialAccount OperatingZar = WithId(new FinancialAccount("Operating", FinancialAccountType.Bank, Currency.ZAR, Ownership.Company, null, null, Now), 10);
    private static readonly FinancialAccount PayoutsUsd = WithId(new FinancialAccount("Payouts", FinancialAccountType.Bank, Currency.USD, Ownership.Company, null, null, Now), 12);
    private static readonly Fund Operating = WithId(new Fund("Operating Capital", null, Now), 2);
    private static int _nextTransactionId = 500;

    private static LedgerTransaction Expense(FinancialAccount account, decimal amount, string date) => WithId(
        LedgerTransaction.Expense(account, Operating, Money.Of(amount, account.Currency), new TransactionDetails(DateOnly.Parse(date), "Paid"),
            new RecordingInfo(1, Now, Guid.NewGuid().ToString("N"), "hash")),
        Interlocked.Increment(ref _nextTransactionId));

    private static Obligation Rent(ObligationRecurrence recurrence = ObligationRecurrence.Monthly, string first = "2026-01-31") =>
        new("Office rent", Money.Of(8500, Currency.ZAR), "Rent", recurrence, DateOnly.Parse(first), null, Now);

    [Fact]
    public void A_monthly_due_date_counts_from_the_first_so_the_31st_never_drifts()
    {
        var rent = Rent();

        Assert.Equal(DateOnly.Parse("2026-01-31"), rent.DueDate);
        rent.RecordPayment(Expense(OperatingZar, 8500, "2026-01-30"), false, Now);
        Assert.Equal(DateOnly.Parse("2026-02-28"), rent.DueDate);
        rent.RecordPayment(Expense(OperatingZar, 8500, "2026-02-27"), false, Now);
        Assert.Equal(DateOnly.Parse("2026-03-31"), rent.DueDate);   // back to the 31st, not the 28th

        Assert.Equal(DateOnly.Parse("2026-07-31"), Obligation.DueAfter(DateOnly.Parse("2026-01-31"), ObligationRecurrence.Quarterly, 2));
        Assert.Equal(DateOnly.Parse("2028-02-29"), Obligation.DueAfter(DateOnly.Parse("2024-02-29"), ObligationRecurrence.Yearly, 4));
    }

    [Fact]
    public void A_payment_is_an_unreversed_expense_in_the_same_currency_and_records_what_was_actually_paid()
    {
        var rent = Rent();

        var payment = rent.RecordPayment(Expense(OperatingZar, 8650, "2026-01-29"), false, Now);
        Assert.Equal((DateOnly.Parse("2026-01-31"), DateOnly.Parse("2026-01-29"), 8650m), (payment.DueDate, payment.PaidOn, payment.Amount));

        Assert.Throws<DomainException>(() => rent.RecordPayment(Expense(PayoutsUsd, 500, "2026-02-20"), false, Now));
        Assert.Throws<DomainException>(() => rent.RecordPayment(Expense(OperatingZar, 8500, "2026-02-20"), isReversed: true, Now));
        var income = WithId(LedgerTransaction.Income(OperatingZar, Operating, Money.Of(10, Currency.ZAR), new TransactionDetails(DateOnly.Parse("2026-02-20"), "In"),
            new RecordingInfo(1, Now, Guid.NewGuid().ToString("N"), "hash")), 999);
        Assert.Throws<DomainException>(() => rent.RecordPayment(income, false, Now));
        Assert.Single(rent.Payments);   // nothing refused was kept
    }

    [Fact]
    public void A_one_off_is_completed_by_its_payment_and_undoing_the_latest_payment_puts_the_date_back()
    {
        var licence = Rent(ObligationRecurrence.None, "2026-10-15");
        licence.RecordPayment(Expense(OperatingZar, 8500, "2026-10-10"), false, Now);
        Assert.Equal(ObligationStatus.Completed, licence.Status);
        Assert.Throws<DomainException>(() => licence.RecordPayment(Expense(OperatingZar, 8500, "2026-10-11"), false, Now));

        licence.UndoLastPayment(Now);
        Assert.Equal((ObligationStatus.Active, DateOnly.Parse("2026-10-15")), (licence.Status, licence.DueDate));

        var rent = Rent();
        rent.RecordPayment(Expense(OperatingZar, 8500, "2026-01-30"), false, Now);
        rent.RecordPayment(Expense(OperatingZar, 8500, "2026-02-27"), false, Now);
        var undone = rent.UndoLastPayment(Now);
        Assert.Equal(DateOnly.Parse("2026-02-28"), undone.DueDate);
        Assert.Equal(DateOnly.Parse("2026-02-28"), rent.DueDate);
        Assert.Throws<DomainException>(() => Rent().UndoLastPayment(Now));
    }

    [Fact]
    public void Standing_is_overdue_before_today_due_soon_within_14_days_and_closed_ones_say_so()
    {
        var today = DateOnly.Parse("2026-10-29");
        Assert.Equal(ObligationStanding.Overdue, Rent(first: "2026-10-28").StandingOn(today));
        Assert.Equal(ObligationStanding.DueSoon, Rent(first: "2026-10-29").StandingOn(today));
        Assert.Equal(ObligationStanding.DueSoon, Rent(first: "2026-11-12").StandingOn(today));
        Assert.Equal(ObligationStanding.Upcoming, Rent(first: "2026-11-13").StandingOn(today));

        var cancelled = Rent();
        cancelled.Cancel(Now);
        Assert.Equal(ObligationStanding.Cancelled, cancelled.StandingOn(today));
        Assert.Throws<DomainException>(() => cancelled.RecordPayment(Expense(OperatingZar, 1, "2026-01-30"), false, Now));
    }

    [Fact]
    public void The_schedule_and_currency_are_fixed_once_paid_and_details_are_validated()
    {
        var rent = Rent();
        rent.Update("Rent", Money.Of(9000, Currency.ZAR), null, ObligationRecurrence.Quarterly, DateOnly.Parse("2026-02-01"), "Raised", Now);
        Assert.Equal((ObligationRecurrence.Quarterly, DateOnly.Parse("2026-02-01"), 9000m), (rent.Recurrence, rent.DueDate, rent.Amount));

        rent.RecordPayment(Expense(OperatingZar, 9000, "2026-02-01"), false, Now);
        Assert.Throws<DomainException>(() => rent.Update("Rent", Money.Of(9000, Currency.ZAR), null, ObligationRecurrence.Monthly, DateOnly.Parse("2026-02-01"), null, Now));
        Assert.Throws<DomainException>(() => rent.Update("Rent", Money.Of(500, Currency.USD), null, ObligationRecurrence.Quarterly, DateOnly.Parse("2026-02-01"), null, Now));
        rent.Update("Rent (renewed)", Money.Of(9500, Currency.ZAR), "Rent", ObligationRecurrence.Quarterly, DateOnly.Parse("2026-02-01"), null, Now);
        Assert.Equal(DateOnly.Parse("2026-05-01"), rent.DueDate);   // still counted from the first date

        Assert.Throws<DomainException>(() => new Obligation(" ", Money.Of(1, Currency.ZAR), null, ObligationRecurrence.None, DateOnly.Parse("2026-11-01"), null, Now));
        Assert.Throws<DomainException>(() => new Obligation("Zero", Money.Of(0, Currency.ZAR), null, ObligationRecurrence.None, DateOnly.Parse("2026-11-01"), null, Now));
        Assert.Throws<DomainException>(() => new Obligation("Odd", Money.Of(1, Currency.ZAR), null, (ObligationRecurrence)9, DateOnly.Parse("2026-11-01"), null, Now));
    }

    [Fact]
    public void A_fund_minimum_and_target_are_in_one_currency_and_must_make_sense()
    {
        var fund = new Fund("Operating Capital", null, Now);
        fund.SetThresholds(Currency.ZAR, 20000, 50000);
        Assert.Equal((Currency.ZAR, 20000m, 50000m), (fund.ThresholdCurrency, fund.Minimum, fund.Target));

        fund.SetThresholds(Currency.ZAR, null, null);
        Assert.Null(fund.ThresholdCurrency);   // clearing both clears the currency

        Assert.Throws<DomainException>(() => fund.SetThresholds(null, 100, null));
        Assert.Throws<DomainException>(() => fund.SetThresholds(Currency.ZAR, -1, null));
        Assert.Throws<DomainException>(() => fund.SetThresholds(Currency.ZAR, null, 0));
        Assert.Throws<DomainException>(() => fund.SetThresholds(Currency.ZAR, 500, 100));
        Assert.Throws<DomainException>(() => fund.SetThresholds(Currency.ZAR, 10.005m, null));
        var unallocated = WithId(new Fund("Unallocated", null, Now), 1);
        typeof(Fund).GetProperty(nameof(Fund.IsSystem))!.SetValue(unallocated, true);
        Assert.Throws<DomainException>(() => unallocated.SetThresholds(Currency.ZAR, 100, null));
    }

    private static T WithId<T>(T entity, int id)
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
        return entity;
    }
}
