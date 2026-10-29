using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

// An ISO 4217 currency and the number of minor units (decimal places) its amounts are kept in.
// Only currencies the business actually uses are supported, so a typo can never create a new currency.
public sealed record Currency
{
    public static readonly Currency ZAR = new("ZAR", 2);
    public static readonly Currency USD = new("USD", 2);
    public static readonly Currency EUR = new("EUR", 2);
    public static readonly Currency GBP = new("GBP", 2);

    private static readonly Dictionary<string, Currency> SupportedByCode =
        new[] { ZAR, USD, EUR, GBP }.ToDictionary(c => c.Code, StringComparer.Ordinal);

    public string Code { get; }
    public int MinorUnits { get; }

    private Currency(string code, int minorUnits)
    {
        Code = code;
        MinorUnits = minorUnits;
    }

    public static IReadOnlyCollection<Currency> Supported => SupportedByCode.Values;

    public static Currency FromCode(string code) =>
        TryFromCode(code, out var currency)
            ? currency
            : throw new DomainException($"Currency '{code}' is not supported. Supported: {string.Join(", ", SupportedByCode.Keys)}.");

    public static bool TryFromCode(string? code, out Currency currency)
    {
        var normalised = code?.Trim().ToUpperInvariant() ?? string.Empty;
        return SupportedByCode.TryGetValue(normalised, out currency!);
    }

    // The smallest representable amount, e.g. 0.01 for two minor units
    public decimal SmallestUnit => new(1, 0, 0, isNegative: false, scale: (byte)MinorUnits);

    public bool IsWholeMinorUnits(decimal amount) => decimal.Round(amount, MinorUnits) == amount;

    public override string ToString() => Code;
}
