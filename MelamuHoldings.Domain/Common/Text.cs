namespace MelamuHoldings.Domain.Common;

// Trims free text and enforces the length limits the database columns allow
public static class Text
{
    public static string Required(string? value, int maxLength, string field)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new DomainException($"{field} is required.");
        return value.Length <= maxLength ? value : throw new DomainException($"{field} must be at most {maxLength} characters.");
    }

    public static string? Optional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= maxLength ? value : throw new DomainException($"{field} must be at most {maxLength} characters.");
    }
}
