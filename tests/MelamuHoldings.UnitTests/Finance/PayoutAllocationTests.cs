using System.Globalization;
using System.Reflection;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

public class AllocationCalculatorTests
{
    private static readonly decimal[] Default = [40, 30, 20, 10];

    // PLAN.md §3.4: largest remainder, ties to the earlier line, never a cent created or lost
    [Theory]
    [InlineData("1350.00", "540.00", "405.00", "270.00", "135.00")]
    [InlineData("1000.01", "400.01", "300.00", "200.00", "100.00")]
    [InlineData("0.03", "0.01", "0.01", "0.01", "0.00")]
    [InlineData("0.01", "0.01", "0.00", "0.00", "0.00")]
    [InlineData("999999.99", "399999.99", "300000.00", "200000.00", "100000.00")]  // remainders .6 .7 .8 .9: the 3 spare cents go to the last three lines
    public void Splits_by_largest_remainder(string amount, params string[] expected)
    {
        static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
        var shares = AllocationCalculator.Split(Money.Of(Parse(amount), Currency.USD), Default);

        Assert.Equal(expected.Select(Parse), shares.Select(s => s.Amount));
        Assert.Equal(Parse(amount), shares.Sum(s => s.Amount));
    }

    [Fact]
    public void Ties_go_to_the_earlier_line()
    {
        // 0.02 three ways: each share is 0.00666…, so the two spare cents go to lines one and two
        var shares = AllocationCalculator.Split(Money.Of(0.02m, Currency.ZAR), [33.3333m, 33.3333m, 33.3334m]);

        Assert.Equal([0.01m, 0.00m, 0.01m], shares.Select(s => s.Amount));
    }

    [Fact]
    public void Every_amount_is_conserved_for_many_amounts_and_odd_percentages()
    {
        decimal[] odd = [12.5m, 37.25m, 0.75m, 49.5m];
        for (var cents = 1; cents <= 5000; cents += 7)
        {
            var amount = Money.Of(cents / 100m, Currency.ZAR);
            Assert.Equal(amount, Money.Sum(AllocationCalculator.Split(amount, odd), Currency.ZAR));
        }
    }

    [Fact]
    public void Rejects_percentages_that_do_not_total_100()
    {
        Assert.Throws<DomainException>(() => AllocationCalculator.Split(Money.Of(100, Currency.USD), [50, 40]));
    }
}

public class AllocationPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Lines_must_total_exactly_100_percent()
    {
        var ex = Assert.Throws<DomainException>(() => Policy((Funds.Operating, 40), (Funds.Continuity, 30), (Funds.Wealth, 20)));
        Assert.Contains("add up to 90%", ex.Message);
    }

    [Fact]
    public void Rejects_the_unallocated_fund_duplicates_zero_and_excess_precision()
    {
        Assert.Throws<DomainException>(() => Policy((Funds.Unallocated, 10), (Funds.Operating, 90)));
        Assert.Throws<DomainException>(() => Policy((Funds.Operating, 50), (Funds.Operating, 50)));
        Assert.Throws<DomainException>(() => Policy((Funds.Operating, 100), (Funds.Continuity, 0)));
        Assert.Throws<DomainException>(() => Policy((Funds.Operating, 33.33333m), (Funds.Continuity, 66.66667m)));
    }

    [Fact]
    public void Keeps_line_order_for_splitting()
    {
        var policy = Policy((Funds.Owner, 50), (Funds.Operating, 50));

        Assert.Equal([Funds.Owner.Id, Funds.Operating.Id], policy.Lines.OrderBy(l => l.Position).Select(l => l.FundId));
        // Equal remainders: the odd cent goes to the line listed first
        Assert.Equal([0.01m, 0.00m], policy.Split(Money.Of(0.01m, Currency.USD)).Select(s => s.Amount));
    }

    private static AllocationPolicy Policy(params (Fund Fund, decimal Percentage)[] lines) => new(2, lines, Now, userId: 1);
}

public class PayoutTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    [Fact]
    public void Moves_from_requested_to_approved_to_received_and_records_what_was_credited()
    {
        var payout = Requested(1350);
        payout.Approve(null, Today, Now);
        var receipt = Saved(payout.BuildReceipt(Funds.PayoutsUsd, Funds.Unallocated, Usd(1340), Usd(10), Today, "WIRE-1", Recording()), 40);
        payout.MarkReceived(receipt, Funds.PayoutsUsd);

        Assert.Equal(PayoutStatus.Received, payout.Status);
        Assert.Equal(Usd(1340), payout.Credited);
        Assert.Equal(10m, payout.FeeAmount);
        Assert.Null(payout.EffectiveRate);
        // Gross income and the fee are both on record; the account nets to what arrived
        Assert.Equal([Usd(1350), Usd(-10)], receipt.Entries.Select(e => e.Money));
        Assert.Equal([LedgerEntryRole.Principal, LedgerEntryRole.Fee], receipt.Entries.Select(e => e.Role));
    }

    [Fact]
    public void A_payout_must_be_approved_before_it_is_received()
    {
        var payout = Requested(500);

        var ex = Assert.Throws<DomainException>(() =>
            payout.BuildReceipt(Funds.PayoutsUsd, Funds.Unallocated, Usd(500), null, Today, null, Recording()));
        Assert.Contains("approved payout", ex.Message);
    }

    [Fact]
    public void Approval_cannot_exceed_the_request_and_receipt_cannot_exceed_the_approval()
    {
        var payout = Requested(1000);
        Assert.Throws<DomainException>(() => payout.Approve(Usd(1000.01m), Today, Now));

        payout.Approve(Usd(900), Today, Now);
        Assert.Throws<DomainException>(() =>
            payout.BuildReceipt(Funds.PayoutsUsd, Funds.Unallocated, Usd(890), Usd(20), Today, null, Recording()));
    }

    [Fact]
    public void Credited_in_another_currency_records_the_effective_rate_from_the_amounts()
    {
        var payout = Requested(1000);
        payout.Approve(null, Today, Now);
        var receipt = Saved(payout.BuildReceipt(Funds.OperatingZar, Funds.Unallocated, Zar(18100), Zar(150), Today, null, Recording()), 41);
        payout.MarkReceived(receipt, Funds.OperatingZar);

        Assert.Equal(Zar(18100), payout.Credited);
        Assert.Equal(18.25m, payout.EffectiveRate);  // (18 100 + 150) ÷ 1 000
    }

    [Fact]
    public void Rejection_is_only_for_payouts_not_yet_received_and_needs_a_reason()
    {
        var rejected = Requested(100);
        Assert.Throws<DomainException>(() => rejected.Reject(" ", Today, Now));
        rejected.Reject("Breached daily loss rule", Today, Now);
        Assert.Equal(PayoutStatus.Rejected, rejected.Status);
        Assert.Throws<DomainException>(() => rejected.Approve(null, Today, Now));

        var received = Requested(100);
        received.Approve(null, Today, Now);
        received.MarkReceived(Saved(received.BuildReceipt(Funds.PayoutsUsd, Funds.Unallocated, Usd(100), null, Today, null, Recording()), 42), Funds.PayoutsUsd);
        Assert.Throws<DomainException>(() => received.Reject("Too late", Today, Now));
    }

    [Fact]
    public void Undoing_a_receipt_returns_the_payout_to_approved()
    {
        var payout = Requested(100);
        payout.Approve(null, Today, Now);
        payout.MarkReceived(Saved(payout.BuildReceipt(Funds.PayoutsUsd, Funds.Unallocated, Usd(100), null, Today, null, Recording()), 43), Funds.PayoutsUsd);

        payout.UndoReceipt();

        Assert.Equal(PayoutStatus.Approved, payout.Status);
        Assert.Null(payout.IncomeTransactionId);
        Assert.Null(payout.Credited);
    }

    private static Payout Requested(decimal amount) => new("Prop firm", null, null, Usd(amount), Today, Recording());

    internal static RecordingInfo Recording() => new(1, Now, Guid.NewGuid().ToString("N"), "hash");
    internal static Money Usd(decimal amount) => Money.Of(amount, Currency.USD);
    internal static Money Zar(decimal amount) => Money.Of(amount, Currency.ZAR);
    internal static T Saved<T>(T entity, int id) => Funds.WithId(entity, id);
}

public class AllocationTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);
    private static readonly IReadOnlySet<int> NoneReversed = new HashSet<int>();

    [Fact]
    public void Confirming_snapshots_fund_names_percentages_and_amounts()
    {
        var allocation = Confirmed(1350);

        Assert.Equal(["Operating Capital", "Business Continuity", "Wealth Fund", "Owner's Distribution"], allocation.Lines.Select(l => l.FundName));
        Assert.Equal([540m, 405m, 270m, 135m], allocation.Lines.Select(l => l.Amount));
        Assert.Equal(AllocationStatus.Confirmed, allocation.Status(NoneReversed));
    }

    [Fact]
    public void Only_a_received_payout_can_be_allocated()
    {
        var approved = new Payout("Prop firm", null, null, PayoutTests.Usd(100), Today, PayoutTests.Recording());
        approved.Approve(null, Today, Now);

        Assert.Throws<DomainException>(() => Allocation.Confirm(approved, DefaultPolicy(), Funds.ById, PayoutTests.Recording()));
    }

    [Fact]
    public void Status_follows_settlements_and_a_reversed_settlement_reopens_its_line()
    {
        var allocation = Confirmed(100);
        var lines = allocation.Lines.ToList();

        Settle(allocation, lines[0], 40, transactionId: 101);
        Assert.Equal(AllocationStatus.PartiallySettled, allocation.Status(NoneReversed));

        Settle(allocation, lines[1], 30, 102);
        Settle(allocation, lines[2], 20, 103);
        Settle(allocation, lines[3], 10, 104);
        Assert.Equal(AllocationStatus.Reconciled, allocation.Status(NoneReversed));

        var reversed = new HashSet<int> { 102 };
        Assert.Equal(AllocationStatus.PartiallySettled, allocation.Status(reversed));
        Assert.Equal(PayoutTests.Usd(30), lines[1].Outstanding(reversed));
    }

    [Fact]
    public void A_line_cannot_be_over_settled()
    {
        var allocation = Confirmed(100);
        var line = allocation.Lines[0];
        Settle(allocation, line, 25, 201);

        var ex = Assert.Throws<DomainException>(() => Build(allocation, line, 15.01m, unallocated: 100));
        Assert.Contains("still to settle", ex.Message);
    }

    [Fact]
    public void Settlement_needs_the_money_to_be_unallocated_in_the_source_account()
    {
        var allocation = Confirmed(100);

        var ex = Assert.Throws<DomainException>(() => Build(allocation, allocation.Lines[0], 40, unallocated: 39.99m));
        Assert.Contains("holds only", ex.Message);
    }

    [Fact]
    public void An_earmark_settlement_moves_money_from_unallocated_to_the_line_fund_in_the_same_account()
    {
        var allocation = Confirmed(100);

        var tx = Build(allocation, allocation.Lines[1], 30, unallocated: 100);

        Assert.Equal(LedgerTransactionType.FundReallocation, tx.Type);
        Assert.Equal([(Funds.PayoutsUsd.Id, Funds.Unallocated.Id, -30m), (Funds.PayoutsUsd.Id, Funds.Continuity.Id, 30m)],
            tx.Entries.Select(e => (e.FinancialAccountId, e.FundId, e.Amount)));
    }

    [Fact]
    public void Settled_allocations_cannot_be_cancelled_but_unsettled_ones_can()
    {
        var settled = Confirmed(100);
        Settle(settled, settled.Lines[0], 40, 301);
        Assert.Throws<DomainException>(() => settled.Cancel("Wrong policy", Now, NoneReversed));
        // Once the settlement is reversed, cancelling is allowed again
        settled.Cancel("Wrong policy", Now, new HashSet<int> { 301 });
        Assert.Equal(AllocationStatus.Cancelled, settled.Status(NoneReversed));
    }

    private static AllocationPolicy DefaultPolicy() => Funds.WithId(new AllocationPolicy(1,
        [(Funds.Operating, 40m), (Funds.Continuity, 30m), (Funds.Wealth, 20m), (Funds.Owner, 10m)], Now, 1), 1);

    private static Allocation Confirmed(decimal amount)
    {
        var payout = Funds.WithId(new Payout("Prop firm", null, null, PayoutTests.Usd(amount), Today, PayoutTests.Recording()), 7);
        payout.Approve(null, Today, Now);
        payout.MarkReceived(Funds.WithId(payout.BuildReceipt(Funds.PayoutsUsd, Funds.Unallocated, PayoutTests.Usd(amount), null, Today, null, PayoutTests.Recording()), 50), Funds.PayoutsUsd);
        var allocation = Funds.WithId(Allocation.Confirm(payout, DefaultPolicy(), Funds.ById, PayoutTests.Recording()), 9);
        var id = 1;
        foreach (var line in allocation.Lines) Funds.WithId(line, id++);
        return allocation;
    }

    private static LedgerTransaction Build(Allocation allocation, AllocationLine line, decimal amount, decimal unallocated) =>
        allocation.BuildSettlement(line,
            new SettlementRequestDetails(SettlementMethod.Earmark, PayoutTests.Usd(amount), Today, null, null),
            Funds.PayoutsUsd, Funds.Unallocated, Funds.ById[line.FundId], null, PayoutTests.Usd(unallocated), NoneReversed, PayoutTests.Recording());

    private static void Settle(Allocation allocation, AllocationLine line, decimal amount, int transactionId)
    {
        var tx = Funds.WithId(Build(allocation, line, amount, unallocated: 1_000_000), transactionId);
        allocation.RecordSettlement(line, tx, SettlementMethod.Earmark, PayoutTests.Usd(amount), Now);
    }
}

public class LedgerFeeTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    [Fact]
    public void An_exchange_fee_is_a_fee_entry_on_the_account_whose_currency_it_is_in()
    {
        var sendingSide = LedgerTransaction.CurrencyExchange(Funds.PayoutsUsd, Funds.Unallocated, PayoutTests.Usd(1000),
            Funds.OperatingZar, Funds.Unallocated, PayoutTests.Zar(18250.50m), new TransactionDetails(Today, "Convert"),
            PayoutTests.Recording(), fee: PayoutTests.Usd(9.50m), quotedRate: 18.30m);

        var fee = Assert.Single(sendingSide.Entries, e => e.Role == LedgerEntryRole.Fee);
        Assert.Equal((Funds.PayoutsUsd.Id, PayoutTests.Usd(-9.50m)), (fee.FinancialAccountId, fee.Money));
        Assert.Equal(18.2505m, sendingSide.ExchangeRate);  // effective: from the amounts, unaffected by the quote
        Assert.Equal(18.30m, sendingSide.QuotedRate);

        var receivingSide = LedgerTransaction.CurrencyExchange(Funds.PayoutsUsd, Funds.Unallocated, PayoutTests.Usd(1000),
            Funds.OperatingZar, Funds.Unallocated, PayoutTests.Zar(18250.50m), new TransactionDetails(Today, "Convert"),
            PayoutTests.Recording(), fee: PayoutTests.Zar(75));
        Assert.Equal(Funds.OperatingZar.Id, Assert.Single(receivingSide.Entries, e => e.Role == LedgerEntryRole.Fee).FinancialAccountId);
    }

    [Fact]
    public void A_fee_in_a_third_currency_or_a_bad_quoted_rate_is_rejected()
    {
        Assert.Throws<DomainException>(() => LedgerTransaction.CurrencyExchange(Funds.PayoutsUsd, Funds.Unallocated, PayoutTests.Usd(100),
            Funds.OperatingZar, Funds.Unallocated, PayoutTests.Zar(1800), new TransactionDetails(Today, "Convert"),
            PayoutTests.Recording(), fee: Money.Of(5, Currency.EUR)));
        Assert.Throws<DomainException>(() => LedgerTransaction.CurrencyExchange(Funds.PayoutsUsd, Funds.Unallocated, PayoutTests.Usd(100),
            Funds.OperatingZar, Funds.Unallocated, PayoutTests.Zar(1800), new TransactionDetails(Today, "Convert"),
            PayoutTests.Recording(), quotedRate: 0));
    }

    [Fact]
    public void Reversal_negates_fee_entries_and_keeps_their_role()
    {
        var income = Funds.WithId(LedgerTransaction.Income(Funds.PayoutsUsd, Funds.Unallocated, PayoutTests.Usd(500),
            new TransactionDetails(Today, "Payout"), PayoutTests.Recording(), PayoutTests.Usd(5)), 60);

        var reversal = income.Reverse(new TransactionDetails(Today, "Wrong amount"), PayoutTests.Recording());

        Assert.Equal([(PayoutTests.Usd(-500), LedgerEntryRole.Principal), (PayoutTests.Usd(5), LedgerEntryRole.Fee)],
            reversal.Entries.Select(e => (e.Money, e.Role)));
    }

    [Fact]
    public void An_income_fee_must_leave_something_credited()
    {
        Assert.Throws<DomainException>(() => LedgerTransaction.Income(Funds.PayoutsUsd, Funds.Unallocated, PayoutTests.Usd(10),
            new TransactionDetails(Today, "Payout"), PayoutTests.Recording(), PayoutTests.Usd(10)));
    }
}

// Accounts and funds with the ids the database would give them
internal static class Funds
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    public static readonly Fund Unallocated = WithId(new Fund("Unallocated", null, Now), 1);
    public static readonly Fund Operating = WithId(new Fund("Operating Capital", null, Now), 2);
    public static readonly Fund Continuity = WithId(new Fund("Business Continuity", null, Now), 3);
    public static readonly Fund Wealth = WithId(new Fund("Wealth Fund", null, Now), 4);
    public static readonly Fund Owner = WithId(new Fund("Owner's Distribution", null, Now), 5);
    public static readonly IReadOnlyDictionary<int, Fund> ById = new[] { Unallocated, Operating, Continuity, Wealth, Owner }.ToDictionary(f => f.Id);

    public static readonly FinancialAccount PayoutsUsd =
        WithId(new FinancialAccount("Payouts USD", FinancialAccountType.Bank, Currency.USD, Ownership.Company, null, null, Now), 20);
    public static readonly FinancialAccount OperatingZar =
        WithId(new FinancialAccount("Operating ZAR", FinancialAccountType.Bank, Currency.ZAR, Ownership.Company, null, null, Now), 21);

    public static T WithId<T>(T entity, int id)
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
        return entity;
    }
}
