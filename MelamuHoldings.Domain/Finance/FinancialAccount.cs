using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

public enum FinancialAccountType { Bank, Cash, Brokerage, Other }

// Who legally owns the money. Never inferred from the account's name: the owner states it.
public enum Ownership { Company, Personal, Unspecified }

public enum FinancialAccountStatus { Active, Closed }

// A real place money is held (a bank account, cash, a brokerage account), in exactly one currency.
// No credentials are ever stored. Balances are not stored either: they are summed from ledger entries.
public class FinancialAccount
{
    public const int MaxNameLength = 100;
    public const int MaxInstitutionLength = 100;
    public const int MaxNotesLength = 500;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public FinancialAccountType Type { get; private set; }
    public Currency Currency { get; private set; } = Currency.ZAR;
    public Ownership Ownership { get; private set; }
    public string? Institution { get; private set; }
    public string? Notes { get; private set; }
    public FinancialAccountStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    private FinancialAccount() { }

    public FinancialAccount(
        string name, FinancialAccountType type, Currency currency, Ownership ownership,
        string? institution, string? notes, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(currency);
        Type = type;
        Currency = currency;
        Status = FinancialAccountStatus.Active;
        CreatedAt = createdAt;
        SetDetails(name, ownership, institution, notes);
    }

    public bool IsClosed => Status == FinancialAccountStatus.Closed;

    // Type and currency are fixed once created: existing entries were recorded in that currency
    public void UpdateDetails(string name, Ownership ownership, string? institution, string? notes) =>
        SetDetails(name, ownership, institution, notes);

    public void Close(Money balance, DateTime closedAt)
    {
        EnsureActive();
        if (balance.Currency != Currency)
            throw new CurrencyMismatchException(Currency, balance.Currency);
        if (!balance.IsZero)
            throw new DomainException($"Account '{Name}' still holds {balance}. Move or record the remaining money before closing it.");

        Status = FinancialAccountStatus.Closed;
        ClosedAt = closedAt;
    }

    public void EnsureActive()
    {
        if (IsClosed)
            throw new DomainException($"Account '{Name}' is closed.");
    }

    private void SetDetails(string name, Ownership ownership, string? institution, string? notes)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxNameLength)
            throw new DomainException($"Account name must be between 1 and {MaxNameLength} characters.");
        institution = Optional(institution, MaxInstitutionLength, "Institution");
        notes = Optional(notes, MaxNotesLength, "Notes");

        Name = name;
        Ownership = ownership;
        Institution = institution;
        Notes = notes;
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= maxLength ? value : throw new DomainException($"{field} must be at most {maxLength} characters.");
    }
}
