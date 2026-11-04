using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

// What an account is, stated by the owner and never inferred, and which accounts count as the business's cash
public class AccountProfileTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static FinancialAccount Account(FinancialAccountType type = FinancialAccountType.Bank, Ownership ownership = Ownership.Company) =>
        new("Account", type, Currency.ZAR, ownership, null, null, Now);

    [Fact]
    public void A_new_account_states_nothing_until_it_is_told()
    {
        var a = Account();

        Assert.Equal((FinancialAccountCategory.NotStated, TaxTreatment.NotStated), (a.Category, a.TaxTreatment));
        Assert.Null(a.Reference);
        Assert.Null(a.OpenedOn);
        Assert.Null(a.HomeFundId);
    }

    [Fact]
    public void A_name_says_nothing_about_tax_or_category()
    {
        var a = new FinancialAccount("EasyEquities TFSA", FinancialAccountType.Brokerage, Currency.ZAR, Ownership.Personal, "EasyEquities", null, Now);

        Assert.Equal((FinancialAccountCategory.NotStated, TaxTreatment.NotStated), (a.Category, a.TaxTreatment));
    }

    [Fact]
    public void The_profile_keeps_what_was_stated()
    {
        var a = Account(FinancialAccountType.Brokerage, Ownership.Personal);
        var opened = new DateOnly(2026, 1, 15);

        a.SetProfile(FinancialAccountCategory.Investment, TaxTreatment.TaxFree, " 1234 ", opened, 4, Now);

        Assert.Equal((FinancialAccountCategory.Investment, TaxTreatment.TaxFree, "1234", opened, 4),
            (a.Category, a.TaxTreatment, a.Reference, a.OpenedOn!.Value, a.HomeFundId!.Value));
    }

    [Theory]
    [InlineData(FinancialAccountType.Brokerage, FinancialAccountCategory.Current)]
    [InlineData(FinancialAccountType.Brokerage, FinancialAccountCategory.Savings)]
    [InlineData(FinancialAccountType.Cash, FinancialAccountCategory.Current)]
    [InlineData(FinancialAccountType.Other, FinancialAccountCategory.Savings)]
    [InlineData(FinancialAccountType.Bank, FinancialAccountCategory.Investment)]
    [InlineData(FinancialAccountType.Cash, FinancialAccountCategory.Investment)]
    public void A_category_has_to_suit_the_type(FinancialAccountType type, FinancialAccountCategory category) =>
        Assert.Throws<DomainException>(() => Account(type).SetProfile(category, TaxTreatment.NotStated, null, null, null, Now));

    [Theory]
    [InlineData(FinancialAccountType.Bank, FinancialAccountCategory.Current)]
    [InlineData(FinancialAccountType.Bank, FinancialAccountCategory.Savings)]
    [InlineData(FinancialAccountType.Brokerage, FinancialAccountCategory.Investment)]
    [InlineData(FinancialAccountType.Cash, FinancialAccountCategory.NotStated)]
    public void A_suitable_category_is_kept(FinancialAccountType type, FinancialAccountCategory category)
    {
        var a = Account(type);
        a.SetProfile(category, TaxTreatment.NotStated, null, null, null, Now);
        Assert.Equal(category, a.Category);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("Ending 1234")]
    [InlineData("•••• 1234")]
    [InlineData("Main")]
    public void A_masked_reference_is_accepted(string reference)
    {
        var a = Account();
        a.SetProfile(FinancialAccountCategory.NotStated, TaxTreatment.NotStated, reference, null, null, Now);
        Assert.Equal(reference, a.Reference);
    }

    [Theory]
    [InlineData("62812345678")]
    [InlineData("12345")]
    [InlineData("12 34 56")]
    public void A_full_account_number_is_refused(string reference)
    {
        var ex = Assert.Throws<DomainException>(() => Account().SetProfile(FinancialAccountCategory.NotStated, TaxTreatment.NotStated, reference, null, null, Now));
        Assert.Contains("never stored", ex.Message);
    }

    [Fact]
    public void A_blank_reference_is_empty_and_a_long_one_is_refused()
    {
        var a = Account();
        a.SetProfile(FinancialAccountCategory.NotStated, TaxTreatment.NotStated, "   ", null, null, Now);
        Assert.Null(a.Reference);
        Assert.Throws<DomainException>(() => a.SetProfile(FinancialAccountCategory.NotStated, TaxTreatment.NotStated, new string('x', 21), null, null, Now));
    }

    [Fact]
    public void An_opening_date_cannot_be_in_the_future_but_tomorrow_is_allowed_because_now_is_utc()
    {
        var today = DateOnly.FromDateTime(Now);
        var a = Account();
        a.SetProfile(FinancialAccountCategory.NotStated, TaxTreatment.NotStated, null, today.AddDays(1), null, Now);
        Assert.Throws<DomainException>(() => a.SetProfile(FinancialAccountCategory.NotStated, TaxTreatment.NotStated, null, today.AddDays(2), null, Now));
    }

    [Fact]
    public void Undefined_values_are_refused()
    {
        Assert.Throws<DomainException>(() => Account().SetProfile((FinancialAccountCategory)99, TaxTreatment.NotStated, null, null, null, Now));
        Assert.Throws<DomainException>(() => Account().SetProfile(FinancialAccountCategory.NotStated, (TaxTreatment)99, null, null, null, Now));
    }

    [Theory]
    [InlineData(FinancialAccountType.Bank, Ownership.Company, true)]
    [InlineData(FinancialAccountType.Cash, Ownership.Company, true)]
    [InlineData(FinancialAccountType.Bank, Ownership.Personal, false)]
    [InlineData(FinancialAccountType.Bank, Ownership.Unspecified, false)]
    [InlineData(FinancialAccountType.Cash, Ownership.Personal, false)]
    [InlineData(FinancialAccountType.Brokerage, Ownership.Company, false)]
    [InlineData(FinancialAccountType.Other, Ownership.Company, false)]
    public void Only_a_company_bank_or_cash_account_is_business_cash(FinancialAccountType type, Ownership ownership, bool expected)
    {
        Assert.Equal(expected, FinancialAccount.IsBusinessCash(type, ownership));
        Assert.Equal(expected, Account(type, ownership).CountsAsBusinessCash);
        Assert.Equal(expected, UnrestrictedCash.CountsAccount(type, ownership));
    }

    [Fact]
    public void Changing_the_owner_changes_whether_it_counts()
    {
        var a = Account(ownership: Ownership.Unspecified);
        Assert.False(a.CountsAsBusinessCash);

        a.UpdateDetails("Account", Ownership.Company, null, null);
        Assert.True(a.CountsAsBusinessCash);
    }
}
