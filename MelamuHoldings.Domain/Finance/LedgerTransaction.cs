using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

public enum LedgerTransactionType
{
    OpeningBalance,
    Income,
    Expense,
    Transfer,          // between two accounts, same currency
    FundReallocation,  // earmark: between two funds inside one account; no money leaves the account
    CurrencyExchange,  // between two accounts in different currencies, at a recorded rate
    Reversal
}

// What the person entered about the transaction
public record TransactionDetails(DateOnly Date, string Description, string? Reference = null, string? Category = null);

// Who recorded it, when, and the request identity used to make retries safe
public record RecordingInfo(int UserId, DateTime RecordedAt, string IdempotencyKey, string RequestHash);

// An immutable, dated movement of money. Each entry posts a signed amount to one (account, fund) pair.
// Transactions are only created through the factory methods below, which enforce the rules for each type.
// Once posted they are never edited or deleted; mistakes are corrected with Reverse().
public class LedgerTransaction
{
    public const int MaxDescriptionLength = 200;
    public const int MaxReferenceLength = 100;
    public const int MaxCategoryLength = 50;
    public const int MaxIdempotencyKeyLength = 64;

    private readonly List<LedgerEntry> _entries = new();

    public int Id { get; private set; }
    public LedgerTransactionType Type { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public int RecordedByUserId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? Reference { get; private set; }
    public string? Category { get; private set; }

    // CurrencyExchange only: units of the received currency per one unit of the sent currency
    public decimal? ExchangeRate { get; private set; }

    public int? ReversesTransactionId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    private LedgerTransaction() { }

    private LedgerTransaction(LedgerTransactionType type, TransactionDetails details, RecordingInfo recording)
    {
        Type = type;
        RecordedAt = recording.RecordedAt;
        RecordedByUserId = recording.UserId;
        IdempotencyKey = Required(recording.IdempotencyKey, MaxIdempotencyKeyLength, "Idempotency key");
        RequestHash = recording.RequestHash;

        if (details.Date == default)
            throw new DomainException("Transaction date is required.");
        // A day of slack allows for time zones; anything later is a future-dated entry, which the ledger does not hold
        if (details.Date > DateOnly.FromDateTime(recording.RecordedAt).AddDays(1))
            throw new DomainException("Transaction date cannot be in the future.");

        TransactionDate = details.Date;
        Description = Required(details.Description, MaxDescriptionLength, "Description");
        Reference = Optional(details.Reference, MaxReferenceLength, "Reference");
        Category = Optional(details.Category, MaxCategoryLength, "Category");
    }

    public static LedgerTransaction OpeningBalance(
        FinancialAccount account, Fund fund, Money amount, TransactionDetails details, RecordingInfo recording)
    {
        var tx = new LedgerTransaction(LedgerTransactionType.OpeningBalance, details, recording);
        RequireUsable(account, amount);
        if (amount.IsZero)
            throw new DomainException("An opening balance of zero needs no transaction.");
        tx.Post(account, fund, amount);
        return tx;
    }

    public static LedgerTransaction Income(
        FinancialAccount account, Fund fund, Money amount, TransactionDetails details, RecordingInfo recording)
    {
        var tx = new LedgerTransaction(LedgerTransactionType.Income, details, recording);
        RequireUsable(account, amount);
        RequirePositive(amount);
        tx.Post(account, fund, amount);
        return tx;
    }

    public static LedgerTransaction Expense(
        FinancialAccount account, Fund fund, Money amount, TransactionDetails details, RecordingInfo recording)
    {
        var tx = new LedgerTransaction(LedgerTransactionType.Expense, details, recording);
        RequireUsable(account, amount);
        RequirePositive(amount);
        tx.Post(account, fund, amount.Negate());
        return tx;
    }

    public static LedgerTransaction Transfer(
        FinancialAccount from, Fund fromFund, FinancialAccount to, Fund toFund, Money amount,
        TransactionDetails details, RecordingInfo recording)
    {
        var tx = new LedgerTransaction(LedgerTransactionType.Transfer, details, recording);
        if (from.Id == to.Id)
            throw new DomainException("A transfer needs two different accounts. To earmark money inside one account, use a fund reallocation.");
        if (from.Currency != to.Currency)
            throw new DomainException($"'{from.Name}' is {from.Currency} and '{to.Name}' is {to.Currency}. Record a currency exchange instead.");
        RequireUsable(from, amount);
        RequireUsable(to, amount);
        RequirePositive(amount);
        tx.Post(from, fromFund, amount.Negate());
        tx.Post(to, toFund, amount);
        return tx;
    }

    public static LedgerTransaction FundReallocation(
        FinancialAccount account, Fund fromFund, Fund toFund, Money amount,
        TransactionDetails details, RecordingInfo recording)
    {
        var tx = new LedgerTransaction(LedgerTransactionType.FundReallocation, details, recording);
        if (fromFund.Id == toFund.Id)
            throw new DomainException("A reallocation needs two different funds.");
        RequireUsable(account, amount);
        RequirePositive(amount);
        tx.Post(account, fromFund, amount.Negate());
        tx.Post(account, toFund, amount);
        return tx;
    }

    public static LedgerTransaction CurrencyExchange(
        FinancialAccount from, Fund fromFund, Money sent, FinancialAccount to, Fund toFund, Money received,
        TransactionDetails details, RecordingInfo recording)
    {
        var tx = new LedgerTransaction(LedgerTransactionType.CurrencyExchange, details, recording);
        if (from.Id == to.Id)
            throw new DomainException("A currency exchange moves money between two accounts.");
        if (from.Currency == to.Currency)
            throw new DomainException($"Both accounts are {from.Currency}. Record a transfer instead.");
        RequireUsable(from, sent);
        RequireUsable(to, received);
        RequirePositive(sent);
        RequirePositive(received);

        tx.ExchangeRate = decimal.Round(received.Amount / sent.Amount, 8);
        tx.Post(from, fromFund, sent.Negate());
        tx.Post(to, toFund, received);
        return tx;
    }

    // Posts the exact negation of this transaction. The original stays in the ledger, marked as reversed.
    public LedgerTransaction Reverse(TransactionDetails details, RecordingInfo recording)
    {
        if (Type == LedgerTransactionType.Reversal)
            throw new DomainException("A reversal cannot itself be reversed. Record a new transaction instead.");
        if (details.Date < TransactionDate)
            throw new DomainException($"A reversal cannot be dated before the original transaction ({TransactionDate:yyyy-MM-dd}).");

        var reversal = new LedgerTransaction(LedgerTransactionType.Reversal, details, recording)
        {
            ReversesTransactionId = Id,
            ExchangeRate = ExchangeRate
        };
        foreach (var entry in _entries)
            reversal._entries.Add(new LedgerEntry(entry.FinancialAccountId, entry.FundId, entry.Money.Negate()));
        return reversal;
    }

    private void Post(FinancialAccount account, Fund fund, Money amount) =>
        _entries.Add(new LedgerEntry(account.Id, fund.Id, amount));

    private static void RequireUsable(FinancialAccount account, Money amount)
    {
        account.EnsureActive();
        if (amount.Currency != account.Currency)
            throw new CurrencyMismatchException(account.Currency, amount.Currency);
    }

    private static void RequirePositive(Money amount)
    {
        if (!amount.IsPositive)
            throw new DomainException("Amount must be greater than zero.");
    }

    private static string Required(string? value, int maxLength, string field)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new DomainException($"{field} is required.");
        return value.Length <= maxLength ? value : throw new DomainException($"{field} must be at most {maxLength} characters.");
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= maxLength ? value : throw new DomainException($"{field} must be at most {maxLength} characters.");
    }
}

public class LedgerEntry
{
    public long Id { get; private set; }
    public int LedgerTransactionId { get; private set; }
    public int FinancialAccountId { get; private set; }
    public int FundId { get; private set; }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; } = Currency.ZAR;

    public Money Money => Money.Of(Amount, Currency);

    private LedgerEntry() { }

    internal LedgerEntry(int financialAccountId, int fundId, Money amount)
    {
        FinancialAccountId = financialAccountId;
        FundId = fundId;
        Amount = amount.Amount;
        Currency = amount.Currency;
    }
}
