using System.Reflection;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

// P8.3: what a prop firm account cost and paid back. Only money that moved counts, per currency and unconverted.
public class PropAccountEconomicsTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 11, 1);
    private static readonly FinancialAccount Usd = WithId(new FinancialAccount("Payouts", FinancialAccountType.Bank, Currency.USD, Ownership.Company, null, null, Now), 12);
    private static readonly FinancialAccount Zar = WithId(new FinancialAccount("Operating", FinancialAccountType.Bank, Currency.ZAR, Ownership.Company, null, null, Now), 10);
    private static readonly Fund Unallocated = WithId(new Fund("Unallocated", null, Now), Fund.UnallocatedId);
    private static readonly Fund Operating = WithId(new Fund("Operating Capital", null, Now), 2);
    private static int _nextId = 700;

    private static RecordingInfo Recording() => new(1, Now, Guid.NewGuid().ToString("N"), "hash");

    private static LedgerTransaction Expense(decimal amount = 500m) =>
        WithId(LedgerTransaction.Expense(Usd, Operating, Money.Of(amount, Currency.USD), new TransactionDetails(Day, "Challenge fee"), Recording()), Interlocked.Increment(ref _nextId));

    private static PropFeeAmount Fee(decimal amount, Currency? currency = null, bool reversed = false) => new(currency ?? Currency.USD, amount, reversed);

    private static Payout Requested(decimal amount = 1000m) => new("Firm", null, null, Money.Of(amount, Currency.USD), Day, Recording());

    private static Payout Approved(decimal amount = 1000m, decimal? approved = null)
    {
        var payout = Requested(amount);
        payout.Approve(approved is { } a ? Money.Of(a, Currency.USD) : null, Day, Now);
        return payout;
    }

    private static Payout Received(FinancialAccount into, decimal approved, decimal credited, decimal? fee = null)
    {
        var payout = Approved(approved);
        var receipt = WithId(payout.BuildReceipt(into, Unallocated, Money.Of(credited, into.Currency), fee is { } f ? Money.Of(f, into.Currency) : null, Day, null, Recording()), Interlocked.Increment(ref _nextId));
        payout.MarkReceived(receipt, into);
        return payout;
    }

    [Fact]
    public void Fees_with_no_received_payout_are_what_the_report_flags()
    {
        var result = PropAccountEconomics.Calculate([Fee(500m), Fee(400m)], []);

        Assert.Equal(PropAccountOutcome.FeesWithoutPayouts, result.Outcome);
        var line = Assert.Single(result.Lines);
        Assert.Equal(("USD", 900m, 0m, -900m), (line.Currency.Code, line.Fees, line.PayoutsReceived, line.Net));
        Assert.Equal((2, 0), (result.FeesCounted, result.PayoutsCounted));
    }

    [Fact]
    public void A_received_payout_counts_for_what_reached_the_account()
    {
        var result = PropAccountEconomics.Calculate([Fee(500m)], [Received(Usd, approved: 1000m, credited: 970m, fee: 30m)]);

        Assert.Equal(PropAccountOutcome.PayoutsReceived, result.Outcome);
        var line = Assert.Single(result.Lines);
        Assert.Equal((500m, 970m, 470m), (line.Fees, line.PayoutsReceived, line.Net));
    }

    [Fact]
    public void A_payout_on_its_way_is_never_money_received()
    {
        var result = PropAccountEconomics.Calculate([Fee(500m)], [Requested(1000m), Approved(2000m, approved: 1500m)]);

        Assert.Equal(PropAccountOutcome.FeesWithoutPayouts, result.Outcome);
        Assert.Equal(0m, Assert.Single(result.Lines).PayoutsReceived);
        var awaiting = Assert.Single(result.Awaiting);
        Assert.Equal(("USD", 2500m), (awaiting.Currency.Code, awaiting.Amount));
    }

    [Fact]
    public void A_rejected_payout_counts_for_nothing()
    {
        var rejected = Requested();
        rejected.Reject("Rule breach", Day, Now);

        var result = PropAccountEconomics.Calculate([], [rejected]);

        Assert.Equal(PropAccountOutcome.NothingLinked, result.Outcome);
        Assert.Empty(result.Lines);
        Assert.Empty(result.Awaiting);
    }

    [Fact]
    public void A_reversed_fee_cost_nothing()
    {
        var result = PropAccountEconomics.Calculate([Fee(500m, reversed: true)], []);

        Assert.Equal(PropAccountOutcome.NothingLinked, result.Outcome);
        Assert.Empty(result.Lines);
        Assert.Equal(0, result.FeesCounted);
    }

    [Fact]
    public void Currencies_stay_apart_and_nothing_is_converted()
    {
        var result = PropAccountEconomics.Calculate([Fee(500m)], [Received(Zar, approved: 1000m, credited: 18_000m)]);

        Assert.Equal(PropAccountOutcome.PayoutsReceived, result.Outcome);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(("USD", 500m, 0m, -500m), (result.Lines[0].Currency.Code, result.Lines[0].Fees, result.Lines[0].PayoutsReceived, result.Lines[0].Net));
        Assert.Equal(("ZAR", 0m, 18_000m, 18_000m), (result.Lines[1].Currency.Code, result.Lines[1].Fees, result.Lines[1].PayoutsReceived, result.Lines[1].Net));
    }

    [Fact]
    public void Totals_add_up_per_currency_across_accounts()
    {
        var first = PropAccountEconomics.Calculate([Fee(500m)], []);
        var second = PropAccountEconomics.Calculate([Fee(100m), Fee(200m, Currency.ZAR)], [Received(Usd, 1000m, 1000m)]);

        var totals = PropAccountEconomics.Totals([first, second]);

        Assert.Equal(("USD", 600m, 1000m, 400m), (totals[0].Currency.Code, totals[0].Fees, totals[0].PayoutsReceived, totals[0].Net));
        Assert.Equal(("ZAR", 200m, 0m, -200m), (totals[1].Currency.Code, totals[1].Fees, totals[1].PayoutsReceived, totals[1].Net));
    }

    [Fact]
    public void A_fee_link_is_an_unreversed_expense_for_a_stated_reason()
    {
        var expense = Expense();
        var fee = new PropAccountFee(5, PropAccountFeeKind.Reset, expense, false, 1, Now);
        Assert.Equal((5, expense.Id, PropAccountFeeKind.Reset, Now, 1), (fee.PropFirmAccountId, fee.LedgerTransactionId, fee.Kind, fee.LinkedAt, fee.LinkedByUserId));

        Assert.Throws<DomainException>(() => new PropAccountFee(5, PropAccountFeeKind.Reset, expense, isReversed: true, 1, Now));
        Assert.Throws<DomainException>(() => new PropAccountFee(5, (PropAccountFeeKind)9, expense, false, 1, Now));
        var income = WithId(LedgerTransaction.Income(Usd, Unallocated, Money.Of(10m, Currency.USD), new TransactionDetails(Day, "In"), Recording()), 998);
        Assert.Throws<DomainException>(() => new PropAccountFee(5, PropAccountFeeKind.Other, income, false, 1, Now));
    }

    [Fact]
    public void A_payout_links_to_one_account_and_never_once_rejected()
    {
        var payout = Requested();
        payout.LinkToPropAccount(5);
        Assert.Equal(5, payout.PropFirmAccountId);

        Assert.Throws<DomainException>(() => payout.LinkToPropAccount(5));
        Assert.Throws<DomainException>(() => payout.LinkToPropAccount(6));

        payout.UnlinkFromPropAccount();
        Assert.Null(payout.PropFirmAccountId);
        Assert.Throws<DomainException>(() => payout.UnlinkFromPropAccount());

        payout.Reject("Rule breach", Day, Now);
        Assert.Throws<DomainException>(() => payout.LinkToPropAccount(5));
    }

    private static T WithId<T>(T entity, int id) where T : class
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
        return entity;
    }
}
