using System.Reflection;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

// Phase 7.4: budget lines, and planned purchases as links to expenses already in the ledger
public class BudgetAndPurchaseTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly FinancialAccount OperatingZar = WithId(new FinancialAccount("Operating", FinancialAccountType.Bank, Currency.ZAR, Ownership.Company, null, null, Now), 10);
    private static readonly FinancialAccount PayoutsUsd = WithId(new FinancialAccount("Payouts", FinancialAccountType.Bank, Currency.USD, Ownership.Company, null, null, Now), 12);
    private static readonly Fund Operating = WithId(new Fund("Operating Capital", null, Now), 2);
    private static int _nextTransactionId = 700;

    private static LedgerTransaction Expense(FinancialAccount account, decimal amount, string date = "2026-11-01") => WithId(
        LedgerTransaction.Expense(account, Operating, Money.Of(amount, account.Currency), new TransactionDetails(DateOnly.Parse(date), "Bought"),
            new RecordingInfo(1, Now, Guid.NewGuid().ToString("N"), "hash")),
        Interlocked.Increment(ref _nextTransactionId));

    private static PlannedPurchase Laptop() => new("Laptop", Money.Of(10000, Currency.ZAR), Operating, "Equipment", new DateOnly(2026, 12, 1), null, Now);

    [Fact]
    public void A_budget_line_is_a_positive_amount_for_a_whole_month_in_one_currency()
    {
        var line = new BudgetLine(October, "  Rent ", Money.Of(8000, Currency.ZAR), Now);
        Assert.Equal(("Rent", 8000m, Currency.ZAR), (line.Category, line.Amount, line.Currency));

        Assert.Throws<DomainException>(() => new BudgetLine(new DateOnly(2026, 10, 2), "Rent", Money.Of(1, Currency.ZAR), Now));
        Assert.Throws<DomainException>(() => new BudgetLine(October, " ", Money.Of(1, Currency.ZAR), Now));
        Assert.Throws<DomainException>(() => new BudgetLine(October, "Rent", Money.Of(0, Currency.ZAR), Now));
        Assert.Throws<DomainException>(() => new BudgetLine(October, new string('x', LedgerTransaction.MaxCategoryLength + 1), Money.Of(1, Currency.ZAR), Now));

        line.Update("Office rent", Money.Of(9000, Currency.ZAR), Now);
        Assert.Equal(("Office rent", 9000m, Now), (line.Category, line.Amount, line.UpdatedAt));
        Assert.Throws<DomainException>(() => line.Update("Rent", Money.Of(1, Currency.USD), Now));
        Assert.Equal(9000m, line.Amount);
    }

    [Fact]
    public void Categories_match_whatever_their_case_or_surrounding_spaces()
    {
        Assert.Equal(BudgetLine.CategoryKey("Rent"), BudgetLine.CategoryKey("  rENT "));
        Assert.NotEqual(BudgetLine.CategoryKey("Rent"), BudgetLine.CategoryKey("Office rent"));
        Assert.Equal(string.Empty, BudgetLine.CategoryKey(null));
    }

    [Fact]
    public void A_purchase_goes_from_proposed_to_committed_to_purchased_and_records_what_it_cost()
    {
        var laptop = Laptop();
        Assert.Equal(PlannedPurchaseStatus.Proposed, laptop.Status);
        Assert.Throws<DomainException>(() => laptop.RecordPurchase(Expense(OperatingZar, 9800), false, Now));   // not committed yet
        Assert.Throws<DomainException>(() => laptop.WithdrawCommitment(Now));

        laptop.Commit(Now);
        Assert.Throws<DomainException>(() => laptop.Commit(Now));
        Assert.Throws<DomainException>(() => laptop.Update("Laptop", Money.Of(1, Currency.ZAR), Operating, null, null, null, Now));

        var expense = Expense(OperatingZar, 9800);
        laptop.RecordPurchase(expense, false, Now);
        Assert.Equal((PlannedPurchaseStatus.Purchased, expense.Id, 9800m, new DateOnly(2026, 11, 1), 10000m),
            (laptop.Status, laptop.LedgerTransactionId, laptop.PaidAmount, laptop.PurchasedOn, laptop.Amount));
        Assert.False(laptop.IsOpen);
        Assert.Throws<DomainException>(() => laptop.Cancel(Now));

        laptop.UndoPurchase(Now);
        Assert.Equal((PlannedPurchaseStatus.Committed, (int?)null, (decimal?)null, (DateOnly?)null), (laptop.Status, laptop.LedgerTransactionId, laptop.PaidAmount, laptop.PurchasedOn));
        Assert.Throws<DomainException>(() => laptop.UndoPurchase(Now));
    }

    [Fact]
    public void Only_an_unreversed_expense_in_the_same_currency_can_be_the_purchase_and_nothing_refused_is_kept()
    {
        var laptop = Laptop();
        laptop.Commit(Now);

        Assert.Throws<DomainException>(() => laptop.RecordPurchase(Expense(PayoutsUsd, 500), false, Now));
        Assert.Throws<DomainException>(() => laptop.RecordPurchase(Expense(OperatingZar, 9800), true, Now));
        var income = WithId(LedgerTransaction.Income(OperatingZar, Operating, Money.Of(10, Currency.ZAR), new TransactionDetails(new DateOnly(2026, 11, 1), "In"),
            new RecordingInfo(1, Now, Guid.NewGuid().ToString("N"), "hash")), 999);
        Assert.Throws<DomainException>(() => laptop.RecordPurchase(income, false, Now));

        Assert.Equal((PlannedPurchaseStatus.Committed, (int?)null), (laptop.Status, laptop.LedgerTransactionId));
    }

    [Fact]
    public void A_purchase_needs_a_name_a_positive_amount_and_a_real_fund_and_can_be_cancelled_while_open()
    {
        var unallocated = WithId(new Fund("Unallocated", null, Now), 1);
        typeof(Fund).GetProperty(nameof(Fund.IsSystem))!.SetValue(unallocated, true);

        Assert.Throws<DomainException>(() => new PlannedPurchase(" ", Money.Of(1, Currency.ZAR), Operating, null, null, null, Now));
        Assert.Throws<DomainException>(() => new PlannedPurchase("Desk", Money.Of(0, Currency.ZAR), Operating, null, null, null, Now));
        Assert.Throws<DomainException>(() => new PlannedPurchase("Desk", Money.Of(1, Currency.ZAR), unallocated, null, null, null, Now));

        var proposed = Laptop();
        proposed.Cancel(Now);
        Assert.Equal(PlannedPurchaseStatus.Cancelled, proposed.Status);
        Assert.Throws<DomainException>(() => proposed.Commit(Now));

        var committed = Laptop();
        committed.Commit(Now);
        committed.WithdrawCommitment(Now);
        Assert.Equal(PlannedPurchaseStatus.Proposed, committed.Status);
    }

    private static T WithId<T>(T entity, int id)
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
        return entity;
    }
}
