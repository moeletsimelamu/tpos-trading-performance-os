using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

// What the business plans to spend in one category in one calendar month, in one currency. It is a plan: it never posts
// to the ledger and never blocks an expense. Actual spending comes from expense transactions with the same category.
public class BudgetLine
{
    public int Id { get; private set; }

    // Always the first day of the month
    public DateOnly Month { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; } = Currency.ZAR;
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private BudgetLine() { }

    public BudgetLine(DateOnly month, string category, Money amount, DateTime now)
    {
        if (month.Day != 1) throw new DomainException("A budget belongs to a whole month.");
        Month = month;
        Currency = amount.Currency;
        Set(category, amount);
        CreatedAt = now;
    }

    // The month and currency are fixed: a budget in another currency is another line, and is never converted
    public void Update(string category, Money amount, DateTime now)
    {
        if (amount.Currency != Currency) throw new DomainException($"The budget is in {Currency.Code}; its currency cannot change.");
        Set(category, amount);
        UpdatedAt = now;
    }

    // Categories are free text on the ledger, so they match whatever their case or surrounding spaces
    public static string CategoryKey(string? category) => category?.Trim().ToUpperInvariant() ?? string.Empty;

    private void Set(string category, Money amount)
    {
        if (amount.Amount <= 0) throw new DomainException("The budget must be more than zero.");
        Category = Text.Required(category, LedgerTransaction.MaxCategoryLength, "Category");
        Amount = amount.Amount;
    }
}
