using System.Globalization;
using System.Reflection;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

// Phase 7.5: the formulas in docs/FINANCIAL_DOMAIN.md, tested without a database (PLAN.md §7, the exit criterion for Phase 7)
public class Phase7FormulaTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    // After = available − committed elsewhere − purchase
    [Theory]
    [InlineData(20000, 0, 10000, 5000.0, AffordabilityVerdict.Affordable, 10000, null)]
    [InlineData(15000, 0, 10000, 5000.0, AffordabilityVerdict.Affordable, 5000, null)]            // exactly at the minimum
    [InlineData(14999.99, 0, 10000, 5000.0, AffordabilityVerdict.BreachesMinimum, 4999.99, 0.01)] // a cent below
    [InlineData(20000, 7000, 10000, 5000.0, AffordabilityVerdict.BreachesMinimum, 3000, 2000.0)]  // commitments reserve money
    [InlineData(1000, 0, 3000, 5000.0, AffordabilityVerdict.BreachesMinimum, -2000, 7000.0)]      // the shortfall is to the minimum, not to zero
    [InlineData(10000, 0, 10000, null, AffordabilityVerdict.NoMinimum, 0, null)]                  // exactly zero left is still payable
    [InlineData(9999.99, 0, 10000, null, AffordabilityVerdict.NotEnoughMoney, -0.01, 0.01)]
    [InlineData(-500, 0, 100, null, AffordabilityVerdict.NotEnoughMoney, -600, 600.0)]            // a fund already below zero
    public void A_purchase_is_affordable_when_what_remains_stays_at_or_above_the_minimum(
        double available, double committed, double purchase, double? minimum, AffordabilityVerdict verdict, double after, double? shortfall)
    {
        var answer = Affordability.Evaluate((decimal)available, (decimal)committed, (decimal)purchase, (decimal?)minimum);

        Assert.Equal((verdict, (decimal)after, (decimal?)shortfall), (answer.Verdict, answer.After, answer.Shortfall));
    }

    [Theory]
    [InlineData(70000, 10000, 7.0)]
    [InlineData(5000, 4000, 1.2)]     // 1.25, rounded down so it never overstates
    [InlineData(9999.99, 10000, 0.9)]
    [InlineData(0, 10000, 0)]
    [InlineData(-5000, 10000, 0)]     // a fund below zero covers nothing, not minus months
    public void Coverage_is_counted_money_over_the_monthly_expense_rounded_down(double counted, double expense, double months)
    {
        Assert.Equal((decimal)months, ContinuitySettings.CoverageMonths((decimal)counted, (decimal)expense));
    }

    [Theory]
    [InlineData(240000, 70000, 170000)]
    [InlineData(240000, 240000, 0)]
    [InlineData(240000, 300000, 0)]    // more than the target is not a negative shortfall
    [InlineData(240000, -100, 240100)] // a fund below zero is further from it
    public void The_shortfall_is_what_is_still_missing_and_never_below_zero(double target, double counted, double shortfall)
    {
        Assert.Equal((decimal)shortfall, ContinuitySettings.Shortfall((decimal)target, (decimal)counted));
    }

    [Theory]
    [InlineData(1000, 5000, 20.0)]
    [InlineData(1999.99, 3000, 66.6)]  // rounded down
    [InlineData(6000, 5000, 100)]      // capped
    [InlineData(-10, 5000, 0)]
    public void Restoration_progress_is_the_share_of_its_target_between_0_and_100(double counted, double target, double percent)
    {
        Assert.Equal((decimal)percent, ContinuitySettings.Progress((decimal)counted, (decimal)target));
    }

    [Fact]
    public void A_fund_with_nothing_to_measure_against_has_no_continuity_target()
    {
        var fund = WithId(new Fund("Business Continuity", null, Now), 3);
        var settings = new ContinuitySettings(fund.Id);
        Assert.Null(settings.Target);

        settings.Update(fund, 24, 10000, null, Now);
        Assert.Equal(240000m, settings.Target);
    }

    [Theory]
    [InlineData(8000, 8500, -500, 106.2, true)]    // 106.25, rounded down
    [InlineData(8000, 8000, 0, 100, false)]        // exactly on budget is not over it
    [InlineData(8000, 8000.01, -0.01, 100, true)]  // a cent over is, though it still reads 100%
    [InlineData(1000, 300, 700, 30, false)]
    [InlineData(1000, 0, 1000, 0, false)]
    [InlineData(100, 1300, -1200, 1300, true)]     // not capped
    public void A_budget_line_compares_what_was_spent_with_what_was_planned(double budget, double actual, double remaining, double used, bool exceeded)
    {
        var line = new BudgetLine(new DateOnly(2026, 11, 1), "Rent", Money.Of((decimal)budget, Currency.ZAR), Now);

        Assert.Equal(((decimal)remaining, (decimal)used, exceeded), (line.Remaining((decimal)actual), line.UsedPercent((decimal)actual), line.IsExceeded((decimal)actual)));
    }

    [Fact]
    public void Unrealised_gain_or_loss_is_against_the_ledger_and_the_gain_on_contributions_against_what_went_in()
    {
        // 25,000 contributed, plus 500 of dividends and less 50 of fees, is 25,450 recorded; the broker says 27,000
        var account = new FinancialAccount("Broker", FinancialAccountType.Brokerage, Currency.ZAR, Ownership.Company, null, null, Now);
        var valuation = new InvestmentValuation(account, new DateOnly(2026, 11, 1), 27000, null, 1, Now);

        Assert.Equal(1550m, valuation.Unrealised(25450));
        Assert.Equal(2000m, valuation.GainOnContributions(25000));
        Assert.Equal(-1000m, new InvestmentValuation(account, new DateOnly(2026, 11, 2), 24450, null, 1, Now).Unrealised(25450));
    }

    [Theory]
    [InlineData(LedgerTransactionType.OpeningBalance, LedgerEntryRole.Principal, InvestmentFlow.Contribution)]
    [InlineData(LedgerTransactionType.Transfer, LedgerEntryRole.Principal, InvestmentFlow.Contribution)]
    [InlineData(LedgerTransactionType.CurrencyExchange, LedgerEntryRole.Principal, InvestmentFlow.Contribution)]
    [InlineData(LedgerTransactionType.CurrencyExchange, LedgerEntryRole.Fee, InvestmentFlow.Cost)]
    [InlineData(LedgerTransactionType.Income, LedgerEntryRole.Principal, InvestmentFlow.Income)]
    [InlineData(LedgerTransactionType.Income, LedgerEntryRole.Fee, InvestmentFlow.Cost)]
    [InlineData(LedgerTransactionType.Expense, LedgerEntryRole.Principal, InvestmentFlow.Cost)]
    [InlineData(LedgerTransactionType.FundReallocation, LedgerEntryRole.Principal, InvestmentFlow.None)]
    public void Each_brokerage_entry_falls_in_one_group(LedgerTransactionType type, LedgerEntryRole role, InvestmentFlow flow)
    {
        Assert.Equal(flow, InvestmentValuation.FlowOf(type, role));
    }

    [Theory]
    [InlineData(70000, 5000, 0, 0, 4000, 61000)]
    [InlineData(1445, 0, 405, 500, 0, 540)]         // what remains of a payout is Operating Capital's share
    [InlineData(5000, 5000, 0, 0, 0, 0)]            // exactly at the minimum: nothing is free
    [InlineData(1000, 5000, 0, 0, 0, -4000)]        // below zero: more is spoken for than is held
    [InlineData(0, 0, 0, 0, 0, 0)]
    public void Unrestricted_cash_is_what_is_held_less_what_is_spoken_for(double held, double minimum, double owed, double payouts, double committed, double amount)
    {
        var cash = new UnrestrictedCash((decimal)held, (decimal)minimum, (decimal)owed, (decimal)payouts, (decimal)committed);

        Assert.Equal((decimal)amount, cash.Amount);
    }

    [Fact]
    public void Obligations_are_shown_beside_unrestricted_cash_and_not_deducted_from_it()
    {
        var cash = new UnrestrictedCash(70000, 5000, 0, 0, 4000);

        Assert.Equal(53000m, cash.AfterObligations(8000));
        Assert.Equal(61000m, cash.Amount);
    }

    [Theory]
    [InlineData(Fund.UnallocatedId, true)]
    [InlineData(Fund.OperatingCapitalId, true)]
    [InlineData(3, false)]                          // Business Continuity
    [InlineData(4, false)]                          // Wealth Fund
    public void Only_unallocated_and_operating_money_can_be_unrestricted(int fundId, bool counts)
    {
        Assert.Equal(counts, UnrestrictedCash.CountsFund(fundId));
    }

    [Theory]
    [InlineData(FinancialAccountType.Bank, Ownership.Company, true)]
    [InlineData(FinancialAccountType.Cash, Ownership.Company, true)]
    [InlineData(FinancialAccountType.Bank, Ownership.Personal, false)]
    [InlineData(FinancialAccountType.Bank, Ownership.Unspecified, false)]
    [InlineData(FinancialAccountType.Brokerage, Ownership.Company, false)]
    [InlineData(FinancialAccountType.Other, Ownership.Company, false)]
    public void Only_company_bank_and_cash_accounts_can_hold_unrestricted_cash(FinancialAccountType type, Ownership ownership, bool counts)
    {
        Assert.Equal(counts, UnrestrictedCash.CountsAccount(type, ownership));
    }

    [Fact]
    public void An_amount_too_large_to_store_is_refused()
    {
        Assert.Equal(Money.MaxAmount, Money.Of(Money.MaxAmount, Currency.ZAR).Amount);
        Assert.Equal(-Money.MaxAmount, Money.Of(-Money.MaxAmount, Currency.ZAR).Amount);
        Assert.Throws<DomainException>(() => Money.Of(Money.MaxAmount + 0.01m, Currency.ZAR));
        Assert.Throws<DomainException>(() => Money.Of(-Money.MaxAmount - 0.01m, Currency.ZAR));
    }

    [Fact]
    public void An_obligation_cannot_start_where_its_due_dates_would_run_off_the_calendar()
    {
        var amount = Money.Of(100, Currency.ZAR);
        Assert.Throws<DomainException>(() => new Obligation("Far", amount, null, ObligationRecurrence.Monthly, new DateOnly(Obligation.LastFirstDueYear + 1, 1, 1), null, Now));
        Assert.Equal(Obligation.LastFirstDueYear, new Obligation("Late", amount, null, ObligationRecurrence.Monthly, new DateOnly(Obligation.LastFirstDueYear, 12, 31), null, Now).FirstDueDate.Year);
    }

    [Fact]
    public void Audit_summaries_show_amounts_the_same_whatever_the_servers_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal("4000.00", AuditLog.Amount(4000m));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static T WithId<T>(T entity, int id) where T : class
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
        return entity;
    }
}
