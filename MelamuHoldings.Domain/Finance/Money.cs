using System.Globalization;
using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

public class CurrencyMismatchException : DomainException
{
    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot combine {left} and {right} amounts without a recorded currency exchange.") { }
}

// An amount in a specific currency, always held in whole minor units of that currency.
// Arithmetic only combines amounts of the same currency; converting needs an explicit, recorded exchange.
public sealed record Money : IComparable<Money>
{
    public decimal Amount { get; }
    public Currency Currency { get; }

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    // Rejects sub-unit precision (e.g. 10.005 USD) instead of silently rounding it away
    public static Money Of(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (!currency.IsWholeMinorUnits(amount))
            throw new DomainException(
                $"{amount.ToString(CultureInfo.InvariantCulture)} has more than {currency.MinorUnits} decimal places for {currency}.");
        return new Money(amount, currency);
    }

    // For calculated values (percentages, rates): the caller chooses how to round, so the choice is visible in code
    public static Money Round(decimal amount, Currency currency, MidpointRounding mode)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(decimal.Round(amount, currency.MinorUnits, mode), currency);
    }

    public static Money Zero(Currency currency) => Of(0m, currency);

    public static Money Sum(IEnumerable<Money> amounts, Currency currency) =>
        amounts.Aggregate(Zero(currency), (total, next) => total + next);

    public bool IsZero => Amount == 0m;
    public bool IsPositive => Amount > 0m;
    public bool IsNegative => Amount < 0m;

    public Money Negate() => new(-Amount, Currency);
    public Money Abs() => new(Math.Abs(Amount), Currency);

    public static Money operator +(Money left, Money right) => new(left.Amount + SameCurrency(left, right).Amount, left.Currency);
    public static Money operator -(Money left, Money right) => new(left.Amount - SameCurrency(left, right).Amount, left.Currency);
    public static Money operator -(Money money) => money.Negate();

    public int CompareTo(Money? other)
    {
        if (other is null) return 1;
        return Amount.CompareTo(SameCurrency(this, other).Amount);
    }

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    // Value equality ignores decimal scale: 10.5 and 10.50 USD are equal
    public bool Equals(Money? other) => other is not null && Currency == other.Currency && Amount == other.Amount;
    public override int GetHashCode() => HashCode.Combine(Currency, Amount);

    public override string ToString() =>
        $"{Currency.Code} {Amount.ToString("F" + Currency.MinorUnits, CultureInfo.InvariantCulture)}";

    private static Money SameCurrency(Money left, Money right) =>
        left.Currency == right.Currency ? right : throw new CurrencyMismatchException(left.Currency, right.Currency);
}
