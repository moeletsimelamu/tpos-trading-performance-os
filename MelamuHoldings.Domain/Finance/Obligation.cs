using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

public enum ObligationRecurrence { None, Monthly, Quarterly, Yearly }

public enum ObligationStatus { Active, Completed, Cancelled }

// Where an active obligation stands against today: Overdue, DueSoon (within the warning window) or Upcoming
public enum ObligationStanding { Upcoming, DueSoon, Overdue, Completed, Cancelled }

// Something the business has to pay: once, or every month, quarter or year. It never moves money. A payment is
// recorded by linking an expense the operator has already recorded in the ledger, which moves the next due date on.
// Due dates are counted from the first one (the n-th is FirstDueDate + n periods), so a 31st never drifts to the 28th.
public class Obligation
{
    public const int MaxNameLength = 100;
    public const int MaxNotesLength = 500;
    public const int MaxCategoryLength = 50;
    public const int DueSoonDays = 14;

    // Far enough for any real schedule, and far from the end of the calendar, where counting due dates forward would fail
    public const int LastFirstDueYear = 2100;

    private readonly List<ObligationPayment> _payments = new();

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Currency Currency { get; private set; } = Currency.ZAR;
    public decimal Amount { get; private set; }
    public string? Category { get; private set; }
    public ObligationRecurrence Recurrence { get; private set; }
    public DateOnly FirstDueDate { get; private set; }

    // The next date a payment is due; for a completed one-off, the date it was due
    public DateOnly DueDate { get; private set; }
    public ObligationStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<ObligationPayment> Payments => _payments.OrderBy(p => p.DueDate).ToList();

    private Obligation() { }

    public Obligation(string name, Money amount, string? category, ObligationRecurrence recurrence, DateOnly firstDueDate, string? notes, DateTime now)
    {
        Currency = amount.Currency;
        SetDetails(name, amount, category, notes);
        SetSchedule(recurrence, firstDueDate);
        Status = ObligationStatus.Active;
        CreatedAt = now;
    }

    // The amount's currency is fixed; the schedule can change only until the first payment is recorded
    public void Update(string name, Money amount, string? category, ObligationRecurrence recurrence, DateOnly firstDueDate, string? notes, DateTime now)
    {
        RequireActive();
        if (amount.Currency != Currency) throw new DomainException($"The obligation is in {Currency.Code}; its currency cannot change.");
        if ((recurrence != Recurrence || firstDueDate != FirstDueDate) && _payments.Count > 0)
            throw new DomainException("The schedule cannot change once a payment is recorded. Cancel this obligation and add a new one.");
        SetDetails(name, amount, category, notes);
        if (_payments.Count == 0) SetSchedule(recurrence, firstDueDate);
        UpdatedAt = now;
    }

    // Links an expense already in the ledger as the payment of the date currently due
    public ObligationPayment RecordPayment(LedgerTransaction expense, bool isReversed, DateTime now)
    {
        RequireActive();
        if (expense.Type != LedgerTransactionType.Expense) throw new DomainException("Only an expense can be recorded as a payment.");
        if (isReversed) throw new DomainException($"Transaction {expense.Id} has been reversed, so it did not pay anything.");
        var currencies = expense.Entries.Select(e => e.Currency).Distinct().ToList();
        if (currencies.Count != 1 || currencies[0] != Currency)
            throw new DomainException($"The obligation is in {Currency.Code}; the expense must be in the same currency.");

        var payment = new ObligationPayment(expense.Id, DueDate, expense.TransactionDate, -expense.Entries.Sum(e => e.Amount), now);
        _payments.Add(payment);
        Advance();
        UpdatedAt = now;
        return payment;
    }

    // Removes the most recent payment link (the expense itself stays in the ledger) and puts the due date back
    public ObligationPayment UndoLastPayment(DateTime now)
    {
        if (Status == ObligationStatus.Cancelled) throw new DomainException("A cancelled obligation cannot be changed.");
        var last = _payments.MaxBy(p => p.DueDate) ?? throw new DomainException("There is no payment to undo.");
        _payments.Remove(last);
        Status = ObligationStatus.Active;
        Advance();
        UpdatedAt = now;
        return last;
    }

    public void Cancel(DateTime now)
    {
        RequireActive();
        Status = ObligationStatus.Cancelled;
        UpdatedAt = now;
    }

    public ObligationStanding StandingOn(DateOnly today) => Status switch
    {
        ObligationStatus.Completed => ObligationStanding.Completed,
        ObligationStatus.Cancelled => ObligationStanding.Cancelled,
        _ when DueDate < today => ObligationStanding.Overdue,
        _ when DueDate <= today.AddDays(DueSoonDays) => ObligationStanding.DueSoon,
        _ => ObligationStanding.Upcoming
    };

    // The n-th due date after the first (n = 0 is the first)
    public static DateOnly DueAfter(DateOnly first, ObligationRecurrence recurrence, int n) => recurrence switch
    {
        ObligationRecurrence.Monthly => first.AddMonths(n),
        ObligationRecurrence.Quarterly => first.AddMonths(3 * n),
        ObligationRecurrence.Yearly => first.AddYears(n),
        _ => first
    };

    private void Advance()
    {
        if (Recurrence == ObligationRecurrence.None)
        {
            DueDate = FirstDueDate;
            Status = _payments.Count > 0 ? ObligationStatus.Completed : ObligationStatus.Active;
        }
        else DueDate = DueAfter(FirstDueDate, Recurrence, _payments.Count);
    }

    private void SetSchedule(ObligationRecurrence recurrence, DateOnly firstDueDate)
    {
        if (!Enum.IsDefined(recurrence)) throw new DomainException("Choose how often it is due: once, monthly, quarterly or yearly.");
        if (firstDueDate == default) throw new DomainException("Give the date it is (first) due.");
        if (firstDueDate.Year > LastFirstDueYear) throw new DomainException($"The first due date must be no later than {LastFirstDueYear}.");
        Recurrence = recurrence;
        FirstDueDate = firstDueDate;
        DueDate = firstDueDate;
    }

    private void SetDetails(string name, Money amount, string? category, string? notes)
    {
        Name = Text.Required(name, MaxNameLength, "Obligation name");
        if (amount.Amount <= 0) throw new DomainException("The amount must be more than zero.");
        Amount = amount.Amount;
        Category = Text.Optional(category, MaxCategoryLength, "Category");
        Notes = Text.Optional(notes, MaxNotesLength, "Notes");
    }

    private void RequireActive()
    {
        if (Status != ObligationStatus.Active)
            throw new DomainException(Status == ObligationStatus.Cancelled ? "A cancelled obligation cannot be changed." : "This obligation has been paid in full.");
    }
}

// One payment of an obligation: the expense transaction that paid the date due, and how much it was
public class ObligationPayment
{
    public int Id { get; private set; }
    public int ObligationId { get; private set; }
    public int LedgerTransactionId { get; private set; }
    public DateOnly DueDate { get; private set; }
    public DateOnly PaidOn { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime RecordedAt { get; private set; }

    private ObligationPayment() { }

    internal ObligationPayment(int transactionId, DateOnly dueDate, DateOnly paidOn, decimal amount, DateTime recordedAt)
    {
        LedgerTransactionId = transactionId;
        DueDate = dueDate;
        PaidOn = paidOn;
        Amount = amount;
        RecordedAt = recordedAt;
    }
}
