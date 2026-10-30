using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

// The observed value of a brokerage account on a date, as the owner read it from the broker (PLAN.md §3.6).
// It is not a ledger movement, not income and not cash: it never changes a balance. Comparing it with what the
// ledger recorded for the account up to that date gives the unrealised gain or loss. One valuation per account per day.
public class InvestmentValuation
{
    public const int MaxNoteLength = 200;

    public int Id { get; private set; }
    public int FinancialAccountId { get; private set; }
    public DateOnly ValuedOn { get; private set; }
    public decimal Value { get; private set; }

    // Always the account's currency: the value is never converted
    public Currency Currency { get; private set; } = Currency.ZAR;
    public string? Note { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public int RecordedByUserId { get; private set; }

    private InvestmentValuation() { }

    public InvestmentValuation(FinancialAccount account, DateOnly valuedOn, decimal value, string? note, int userId, DateTime now)
    {
        if (account.Type != FinancialAccountType.Brokerage)
            throw new DomainException("Valuations are recorded for brokerage accounts only; bank and cash balances come from the ledger.");
        if (account.Status != FinancialAccountStatus.Active) throw new DomainException($"Account '{account.Name}' is closed.");
        // A day of slack allows for time zones, as in the ledger
        if (valuedOn > DateOnly.FromDateTime(now).AddDays(1)) throw new DomainException("The valuation date cannot be in the future.");
        if (value < 0) throw new DomainException("A valuation cannot be below zero.");
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > MaxNoteLength) throw new DomainException($"The note can be at most {MaxNoteLength} characters.");

        FinancialAccountId = account.Id;
        ValuedOn = valuedOn;
        Currency = account.Currency;
        Value = Money.Of(value, account.Currency).Amount;   // whole minor units
        Note = trimmed;
        RecordedByUserId = userId;
        RecordedAt = now;
    }
}
