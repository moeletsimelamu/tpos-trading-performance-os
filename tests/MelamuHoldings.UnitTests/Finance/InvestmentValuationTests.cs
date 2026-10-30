using System.Reflection;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

// Phase 7.3: a valuation is an observation of a brokerage account, in its currency, never in the future
public class InvestmentValuationTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private static FinancialAccount Account(FinancialAccountType type, Currency currency)
    {
        var account = new FinancialAccount("Broker", type, currency, Ownership.Company, null, null, Now);
        typeof(FinancialAccount).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(account, 7);
        return account;
    }

    [Fact]
    public void Takes_the_account_currency_and_trims_the_note()
    {
        var valuation = new InvestmentValuation(Account(FinancialAccountType.Brokerage, Currency.USD), Today.AddDays(1), 1250.5m, "  Statement  ", 1, Now);

        Assert.Equal((7, Currency.USD, 1250.5m, "Statement"), (valuation.FinancialAccountId, valuation.Currency, valuation.Value, valuation.Note));
        Assert.Null(new InvestmentValuation(Account(FinancialAccountType.Brokerage, Currency.ZAR), Today, 0, " ", 1, Now).Note);
    }

    [Fact]
    public void Refuses_other_account_types_closed_accounts_future_dates_and_bad_values()
    {
        var broker = Account(FinancialAccountType.Brokerage, Currency.ZAR);

        Assert.Throws<DomainException>(() => new InvestmentValuation(Account(FinancialAccountType.Bank, Currency.ZAR), Today, 100, null, 1, Now));
        Assert.Throws<DomainException>(() => new InvestmentValuation(Account(FinancialAccountType.Cash, Currency.ZAR), Today, 100, null, 1, Now));
        Assert.Throws<DomainException>(() => new InvestmentValuation(broker, Today.AddDays(2), 100, null, 1, Now));
        Assert.Throws<DomainException>(() => new InvestmentValuation(broker, Today, -0.01m, null, 1, Now));
        Assert.Throws<DomainException>(() => new InvestmentValuation(broker, Today, 10.005m, null, 1, Now));
        Assert.Throws<DomainException>(() => new InvestmentValuation(broker, Today, 100, new string('x', InvestmentValuation.MaxNoteLength + 1), 1, Now));

        typeof(FinancialAccount).GetProperty(nameof(FinancialAccount.Status))!.SetValue(broker, FinancialAccountStatus.Closed);
        Assert.Throws<DomainException>(() => new InvestmentValuation(broker, Today, 100, null, 1, Now));
    }
}
