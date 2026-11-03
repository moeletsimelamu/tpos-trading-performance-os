using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

public enum PlannedPurchaseStatus { Proposed, Committed, Purchased, Cancelled }

// Something the business intends to buy, from one fund. Proposed → Committed → Purchased (a link to an expense already in
// the ledger), or Cancelled. It never creates a ledger entry: the expense is recorded in the ledger and linked here.
public class PlannedPurchase
{
    public const int MaxNameLength = 100;
    public const int MaxNotesLength = 500;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; } = Currency.ZAR;
    public int FundId { get; private set; }
    public string? Category { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public string? Notes { get; private set; }
    public PlannedPurchaseStatus Status { get; private set; }

    // Set once purchased: the expense, and what it actually cost, which may differ from the plan
    public int? LedgerTransactionId { get; private set; }
    public decimal? PaidAmount { get; private set; }
    public DateOnly? PurchasedOn { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsOpen => Status is PlannedPurchaseStatus.Proposed or PlannedPurchaseStatus.Committed;

    private PlannedPurchase() { }

    public PlannedPurchase(string name, Money amount, Fund fund, string? category, DateOnly? targetDate, string? notes, DateTime now)
    {
        Status = PlannedPurchaseStatus.Proposed;
        Set(name, amount, fund, category, targetDate, notes);
        CreatedAt = now;
    }

    // Only a proposal can change; a commitment goes back to a proposal first, so a change is always a decision
    public void Update(string name, Money amount, Fund fund, string? category, DateOnly? targetDate, string? notes, DateTime now)
    {
        Require(PlannedPurchaseStatus.Proposed, "Only a proposed purchase can be changed; withdraw the commitment first.");
        Set(name, amount, fund, category, targetDate, notes);
        UpdatedAt = now;
    }

    // Allowed even when it would breach the fund's minimum: the check warns, and the decision is the owner's
    public void Commit(DateTime now)
    {
        Require(PlannedPurchaseStatus.Proposed, "Only a proposed purchase can be committed.");
        Status = PlannedPurchaseStatus.Committed;
        UpdatedAt = now;
    }

    public void WithdrawCommitment(DateTime now)
    {
        Require(PlannedPurchaseStatus.Committed, "Only a committed purchase can go back to proposed.");
        Status = PlannedPurchaseStatus.Proposed;
        UpdatedAt = now;
    }

    public void Cancel(DateTime now)
    {
        if (!IsOpen) throw new DomainException("Only a proposed or committed purchase can be cancelled.");
        Status = PlannedPurchaseStatus.Cancelled;
        UpdatedAt = now;
    }

    // Links an expense already in the ledger; what it cost is recorded even if it differs from the plan
    public void RecordPurchase(LedgerTransaction expense, bool isReversed, DateTime now)
    {
        Require(PlannedPurchaseStatus.Committed, "Commit the purchase before recording it as bought.");
        if (expense.Type != LedgerTransactionType.Expense) throw new DomainException("Only an expense can be recorded as the purchase.");
        if (isReversed) throw new DomainException($"Transaction {expense.Id} has been reversed, so it did not pay for anything.");
        var currencies = expense.Entries.Select(e => e.Currency).Distinct().ToList();
        if (currencies.Count != 1 || currencies[0] != Currency)
            throw new DomainException($"The purchase is in {Currency.Code}; the expense must be in the same currency.");

        LedgerTransactionId = expense.Id;
        PaidAmount = -expense.Entries.Sum(e => e.Amount);
        PurchasedOn = expense.TransactionDate;
        Status = PlannedPurchaseStatus.Purchased;
        UpdatedAt = now;
    }

    // Removes the link and returns the purchase to committed; the expense stays in the ledger
    public void UndoPurchase(DateTime now)
    {
        Require(PlannedPurchaseStatus.Purchased, "Only a purchased item can be undone.");
        LedgerTransactionId = null;
        PaidAmount = null;
        PurchasedOn = null;
        Status = PlannedPurchaseStatus.Committed;
        UpdatedAt = now;
    }

    private void Require(PlannedPurchaseStatus status, string message)
    {
        if (Status != status) throw new DomainException(message);
    }

    private void Set(string name, Money amount, Fund fund, string? category, DateOnly? targetDate, string? notes)
    {
        if (fund.IsSystem) throw new DomainException("Choose the fund it is paid from; Unallocated holds money waiting to be allocated.");
        if (amount.Amount <= 0) throw new DomainException("The amount must be more than zero.");
        Name = Text.Required(name, MaxNameLength, "Purchase name");
        Amount = amount.Amount;
        Currency = amount.Currency;
        FundId = fund.Id;
        Category = Text.Optional(category, LedgerTransaction.MaxCategoryLength, "Category");
        TargetDate = targetDate;
        Notes = Text.Optional(notes, MaxNotesLength, "Notes");
    }
}
