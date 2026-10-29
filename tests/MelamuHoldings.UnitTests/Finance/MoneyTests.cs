using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.UnitTests.Finance;

public class CurrencyTests
{
    [Theory]
    [InlineData("ZAR")]
    [InlineData("usd")]
    [InlineData(" eur ")]
    public void Resolves_supported_codes_case_insensitively(string code)
    {
        var currency = Currency.FromCode(code);

        Assert.Equal(code.Trim().ToUpperInvariant(), currency.Code);
        Assert.Same(currency, Currency.FromCode(currency.Code));
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("")]
    [InlineData("US")]
    public void Rejects_unsupported_codes(string code)
    {
        Assert.Throws<DomainException>(() => Currency.FromCode(code));
        Assert.False(Currency.TryFromCode(code, out _));
    }

    [Fact]
    public void Knows_its_smallest_unit()
    {
        Assert.Equal(0.01m, Currency.ZAR.SmallestUnit);
        Assert.True(Currency.USD.IsWholeMinorUnits(10.50m));
        Assert.False(Currency.USD.IsWholeMinorUnits(10.505m));
    }
}

public class MoneyTests
{
    [Fact]
    public void Rejects_amounts_finer_than_the_currency_minor_unit()
    {
        var error = Assert.Throws<DomainException>(() => Money.Of(10.005m, Currency.USD));

        Assert.Contains("more than 2 decimal places", error.Message);
    }

    [Theory]
    [InlineData(1.005, MidpointRounding.ToEven, 1.00)]
    [InlineData(1.015, MidpointRounding.ToEven, 1.02)]
    [InlineData(1.005, MidpointRounding.AwayFromZero, 1.01)]
    [InlineData(400.004, MidpointRounding.ToZero, 400.00)]
    [InlineData(-0.009, MidpointRounding.ToZero, 0.00)]
    public void Round_uses_the_requested_rounding_mode(double raw, MidpointRounding mode, double expected)
    {
        var money = Money.Round((decimal)raw, Currency.ZAR, mode);

        Assert.Equal((decimal)expected, money.Amount);
    }

    [Fact]
    public void Adds_and_subtracts_within_one_currency()
    {
        var total = Money.Of(1350m, Currency.USD) - Money.Of(405m, Currency.USD) + Money.Of(0.01m, Currency.USD);

        Assert.Equal(Money.Of(945.01m, Currency.USD), total);
    }

    [Fact]
    public void Refuses_to_combine_or_compare_different_currencies()
    {
        var usd = Money.Of(100m, Currency.USD);
        var zar = Money.Of(100m, Currency.ZAR);

        Assert.Throws<CurrencyMismatchException>(() => usd + zar);
        Assert.Throws<CurrencyMismatchException>(() => usd - zar);
        Assert.Throws<CurrencyMismatchException>(() => usd < zar);
        Assert.Throws<CurrencyMismatchException>(() => Money.Sum([usd, zar], Currency.USD));
    }

    [Fact]
    public void Equality_ignores_decimal_scale_but_not_currency()
    {
        var a = Money.Of(10.5m, Currency.USD);
        var b = Money.Of(10.50m, Currency.USD);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Money.Of(10.5m, Currency.ZAR));
    }

    [Fact]
    public void Sums_an_empty_list_to_zero_in_the_given_currency()
    {
        var total = Money.Sum([], Currency.ZAR);

        Assert.True(total.IsZero);
        Assert.Equal(Currency.ZAR, total.Currency);
    }

    [Fact]
    public void Sign_helpers_and_negation()
    {
        var debit = Money.Of(-250m, Currency.ZAR);

        Assert.True(debit.IsNegative);
        Assert.Equal(Money.Of(250m, Currency.ZAR), -debit);
        Assert.Equal(Money.Of(250m, Currency.ZAR), debit.Abs());
        Assert.True(Money.Of(250m, Currency.ZAR) > debit);
    }

    [Fact]
    public void Formats_with_code_and_minor_units_regardless_of_culture()
    {
        Assert.Equal("USD 1350.00", Money.Of(1350m, Currency.USD).ToString());
        Assert.Equal("ZAR -0.50", Money.Of(-0.5m, Currency.ZAR).ToString());
    }
}
