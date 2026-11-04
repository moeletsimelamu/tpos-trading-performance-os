using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

// A payment card is its own record on a bank account, holding only enough to recognise it
public class PaymentCardTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static FinancialAccount Bank() => new("Account", FinancialAccountType.Bank, Currency.ZAR, Ownership.Company, null, null, Now);

    [Fact]
    public void A_card_keeps_only_what_recognises_it()
    {
        var card = Bank().AddCard(PaymentCardKind.Debit, " Business debit ", " 1234 ", Now);

        Assert.Equal((PaymentCardKind.Debit, "Business debit", "1234", PaymentCardStatus.Active, Now), (card.Kind, card.Name, card.LastFour, card.Status, card.AddedAt));
    }

    [Fact]
    public void Name_and_digits_are_optional()
    {
        var card = Bank().AddCard(PaymentCardKind.Credit, "  ", null, Now);

        Assert.Null(card.Name);
        Assert.Null(card.LastFour);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("4111111111111111")]
    [InlineData("12a4")]
    [InlineData("١٢٣٤")]
    public void Anything_but_exactly_four_digits_is_refused(string lastFour) =>
        Assert.Throws<DomainException>(() => Bank().AddCard(PaymentCardKind.Debit, null, lastFour, Now));

    [Theory]
    [InlineData(FinancialAccountType.Brokerage)]
    [InlineData(FinancialAccountType.Cash)]
    [InlineData(FinancialAccountType.Other)]
    public void Only_a_bank_account_can_have_a_card(FinancialAccountType type)
    {
        var account = new FinancialAccount("Account", type, Currency.ZAR, Ownership.Company, null, null, Now);
        Assert.Throws<DomainException>(() => account.AddCard(PaymentCardKind.Debit, null, null, Now));
    }

    [Fact]
    public void An_undefined_kind_and_a_long_name_are_refused()
    {
        Assert.Throws<DomainException>(() => Bank().AddCard((PaymentCardKind)9, null, null, Now));
        Assert.Throws<DomainException>(() => Bank().AddCard(PaymentCardKind.Debit, new string('x', 51), null, Now));
    }

    [Fact]
    public void An_account_cannot_have_two_active_cards_with_the_same_digits()
    {
        var account = Bank();
        account.AddCard(PaymentCardKind.Debit, null, "1234", Now);

        Assert.Throws<DomainException>(() => account.AddCard(PaymentCardKind.Credit, null, "1234", Now));
        account.AddCard(PaymentCardKind.Debit, null, null, Now);
        account.AddCard(PaymentCardKind.Debit, null, null, Now);
        Assert.Equal(3, account.Cards.Count);
    }

    [Fact]
    public void A_card_on_a_closed_account_cannot_be_added()
    {
        var account = Bank();
        account.Close(Money.Zero(Currency.ZAR), Now);

        Assert.Throws<DomainException>(() => account.AddCard(PaymentCardKind.Debit, null, null, Now));
    }

    [Fact]
    public void Closing_the_account_cancels_its_active_cards()
    {
        var account = Bank();
        account.AddCard(PaymentCardKind.Debit, null, "1234", Now);
        var later = Now.AddDays(1);

        account.Close(Money.Zero(Currency.ZAR), later);

        var card = Assert.Single(account.Cards);
        Assert.Equal((PaymentCardStatus.Cancelled, later), (card.Status, card.CancelledAt!.Value));
    }

    [Fact]
    public void A_card_not_on_the_account_or_already_cancelled_is_refused_and_the_digits_can_be_reused()
    {
        var account = Bank();
        Assert.Throws<DomainException>(() => account.CancelCard(99, Now));

        var card = account.AddCard(PaymentCardKind.Debit, null, "1234", Now);
        card.GetType().GetProperty(nameof(PaymentCard.Id))!.SetValue(card, 7);
        account.CancelCard(7, Now);

        Assert.Throws<DomainException>(() => account.CancelCard(7, Now));
        account.AddCard(PaymentCardKind.Debit, null, "1234", Now);
        Assert.Equal(2, account.Cards.Count);
    }
}
