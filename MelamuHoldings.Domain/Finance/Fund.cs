using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

// A purpose that money is earmarked for (Operating Capital, Business Continuity, ...), not a place money is held.
// One bank account can hold money for several funds; fund balances are summed from ledger entries.
public class Fund
{
    public const int UnallocatedId = 1;
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 300;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    // The system "Unallocated" fund receives money that has not been earmarked yet; it cannot be renamed
    public bool IsSystem { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Optional levels the fund should stay above (Minimum) and build towards (Target), in one currency. A fund can hold
    // several currencies; only its balance in ThresholdCurrency is compared with them, and nothing is converted.
    public Currency? ThresholdCurrency { get; private set; }
    public decimal? Minimum { get; private set; }
    public decimal? Target { get; private set; }

    private Fund() { }

    public Fund(string name, string? description, DateTime createdAt)
    {
        SetDetails(name, description);
        CreatedAt = createdAt;
    }

    public void Update(string name, string? description)
    {
        if (IsSystem && name.Trim() != Name)
            throw new DomainException($"The system fund '{Name}' cannot be renamed.");
        SetDetails(name, description);
    }

    public void SetThresholds(Currency? currency, decimal? minimum, decimal? target)
    {
        if (minimum is null && target is null)
        {
            ThresholdCurrency = null;
            Minimum = Target = null;
            return;
        }
        if (IsSystem) throw new DomainException($"The system fund '{Name}' has no minimum or target: money there is waiting to be allocated.");
        if (currency is null) throw new DomainException("Choose the currency the minimum and target are in.");
        if (minimum < 0) throw new DomainException("A minimum cannot be negative.");
        if (target <= 0) throw new DomainException("A target must be more than zero.");
        if (minimum is { } low && target is { } high && high < low) throw new DomainException("The target cannot be below the minimum.");

        // Whole minor units, like every other amount
        ThresholdCurrency = currency;
        Minimum = minimum is { } m ? Money.Of(m, currency).Amount : null;
        Target = target is { } t ? Money.Of(t, currency).Amount : null;
    }

    private void SetDetails(string name, string? description)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxNameLength)
            throw new DomainException($"Fund name must be between 1 and {MaxNameLength} characters.");
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (description?.Length > MaxDescriptionLength)
            throw new DomainException($"Fund description must be at most {MaxDescriptionLength} characters.");

        Name = name;
        Description = description;
    }
}
