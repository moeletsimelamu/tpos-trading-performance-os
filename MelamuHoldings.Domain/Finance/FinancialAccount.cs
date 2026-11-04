using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

public enum FinancialAccountType { Bank, Cash, Brokerage, Other }

// Who legally owns the money. Never inferred from the account's name: the owner states it.
public enum Ownership { Company, Personal, Unspecified }

public enum FinancialAccountStatus { Active, Closed }

// What kind of product the account is, within its type: current and savings for a bank, investment for a brokerage
public enum FinancialAccountCategory { NotStated, Current, Savings, Investment }

// How the money is taxed. Stated by the owner, like ownership, and never inferred from a name: this is what tells a
// TFSA from a normal investment account.
public enum TaxTreatment { NotStated, Taxable, TaxFree }

public enum PaymentCardKind { Debit, Credit }

public enum PaymentCardStatus { Active, Cancelled }

public enum PaymentCardNetwork { NotStated, Visa, Mastercard, Other }

// The colour the card is drawn in, chosen by the owner to match the card in their wallet
public enum PaymentCardFinish { Navy, Graphite, Gold, Silver, Plum, Teal, ForestGold, BlackTeal, BlackGold, Yellow, Green }

// A real place money is held (a bank account, cash, a brokerage account), in exactly one currency.
// No credentials are ever stored. Balances are not stored either: they are summed from ledger entries.
public class FinancialAccount
{
    public const int MaxNameLength = 100;
    public const int MaxInstitutionLength = 100;
    public const int MaxNotesLength = 500;
    public const int MaxReferenceLength = 20;
    public const int MaxReferenceDigits = 4;

    private readonly List<PaymentCard> _cards = new();

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

    public FinancialAccountCategory Category { get; private set; }
    public TaxTreatment TaxTreatment { get; private set; }

    // Enough to recognise the account, such as its last four digits. Never the full number.
    public string? Reference { get; private set; }
    public DateOnly? OpenedOn { get; private set; }

    // The fund this account is mainly for, as a label. It restricts nothing: fund balances still come from the ledger,
    // and an account can hold money for several funds.
    public int? HomeFundId { get; private set; }

    public IReadOnlyList<PaymentCard> Cards => _cards;

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

    // The business's own spendable money: a company bank or cash account. A personal account, one whose ownership is
    // not stated, and a brokerage account are listed apart and never added to it.
    public static bool IsBusinessCash(FinancialAccountType type, Ownership ownership) =>
        type is FinancialAccountType.Bank or FinancialAccountType.Cash && ownership == Ownership.Company;

    public bool CountsAsBusinessCash => IsBusinessCash(Type, Ownership);

    public void SetProfile(FinancialAccountCategory category, TaxTreatment taxTreatment, string? reference, DateOnly? openedOn, int? homeFundId, DateTime now)
    {
        if (!Enum.IsDefined(category)) throw new DomainException("Choose a category: current, savings or investment.");
        if (!Enum.IsDefined(taxTreatment)) throw new DomainException("Choose a tax treatment: taxable or tax-free.");
        if (category is FinancialAccountCategory.Current or FinancialAccountCategory.Savings && Type != FinancialAccountType.Bank)
            throw new DomainException("Only a bank account can be current or savings.");
        if (category == FinancialAccountCategory.Investment && Type != FinancialAccountType.Brokerage)
            throw new DomainException("Only a brokerage account can be an investment account.");
        if (openedOn is { } opened && opened > DateOnly.FromDateTime(now).AddDays(1))
            throw new DomainException("The opening date cannot be in the future.");

        reference = Optional(reference, MaxReferenceLength, "Reference");
        if (reference?.Count(char.IsDigit) > MaxReferenceDigits)
            throw new DomainException($"Enter at most the last {MaxReferenceDigits} digits. The full account number is never stored.");

        Category = category;
        TaxTreatment = taxTreatment;
        Reference = reference;
        OpenedOn = openedOn;
        HomeFundId = homeFundId;
    }

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

        // A card cannot outlive the account it draws on
        foreach (var card in _cards.Where(c => c.Status == PaymentCardStatus.Active))
            card.Cancel(closedAt);
        Status = FinancialAccountStatus.Closed;
        ClosedAt = closedAt;
    }

    // A card a bank has issued on this account, recorded so it can be recognised. A card is not an account and holds no money.
    public PaymentCard AddCard(PaymentCardKind kind, string? name, string? lastFour, DateTime now, PaymentCardNetwork network = PaymentCardNetwork.NotStated, PaymentCardFinish finish = PaymentCardFinish.Navy)
    {
        EnsureActive();
        if (Type != FinancialAccountType.Bank) throw new DomainException("Only a bank account can have a payment card.");
        var card = new PaymentCard(kind, name, lastFour, now, network, finish);
        if (card.LastFour is not null && _cards.Any(c => c.Status == PaymentCardStatus.Active && c.LastFour == card.LastFour))
            throw new DomainException($"This account already has an active card ending {card.LastFour}.");
        _cards.Add(card);
        return card;
    }

    public PaymentCard CancelCard(int cardId, DateTime now)
    {
        var card = _cards.FirstOrDefault(c => c.Id == cardId) ?? throw new DomainException($"Card {cardId} is not on this account.");
        if (card.Status == PaymentCardStatus.Cancelled) throw new DomainException("This card is already cancelled.");
        card.Cancel(now);
        return card;
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

// What identifies a card to its owner, and nothing more: never the full number, expiry, security code or cardholder name.
public class PaymentCard
{
    public const int MaxNameLength = 50;

    public int Id { get; private set; }
    public int FinancialAccountId { get; private set; }
    public PaymentCardKind Kind { get; private set; }
    public string? Name { get; private set; }
    public string? LastFour { get; private set; }
    public PaymentCardNetwork Network { get; private set; }
    public PaymentCardFinish Finish { get; private set; }
    public PaymentCardStatus Status { get; private set; }
    public DateTime AddedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }

    private PaymentCard() { }

    internal PaymentCard(PaymentCardKind kind, string? name, string? lastFour, DateTime now, PaymentCardNetwork network, PaymentCardFinish finish)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("Choose debit or credit.");
        if (!Enum.IsDefined(network)) throw new DomainException("Choose Visa, Mastercard or other.");
        if (!Enum.IsDefined(finish)) throw new DomainException("Choose a colour for the card.");
        Kind = kind;
        Network = network;
        Finish = finish;
        Name = Text.Optional(name, MaxNameLength, "Card name");
        LastFour = Text.Optional(lastFour, 4, "Last four digits");
        if (LastFour is { } digits && (digits.Length != 4 || !digits.All(char.IsAsciiDigit)))
            throw new DomainException("Enter exactly the last four digits of the card, or leave them out.");
        Status = PaymentCardStatus.Active;
        AddedAt = now;
    }

    internal void Cancel(DateTime now)
    {
        Status = PaymentCardStatus.Cancelled;
        CancelledAt = now;
    }
}
