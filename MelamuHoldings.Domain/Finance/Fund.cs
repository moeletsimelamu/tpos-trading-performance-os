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
