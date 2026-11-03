using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

public class SavePlannedPurchaseRequest
{
    [Required, MaxLength(PlannedPurchase.MaxNameLength)] public string Name { get; set; } = string.Empty;
    [Required] public decimal? Amount { get; set; }
    [Required] public string Currency { get; set; } = string.Empty;
    [Required] public int? FundId { get; set; }
    [MaxLength(LedgerTransaction.MaxCategoryLength)] public string? Category { get; set; }
    public DateOnly? TargetDate { get; set; }
    [MaxLength(PlannedPurchase.MaxNotesLength)] public string? Notes { get; set; }
}

public class RecordPurchaseRequest
{
    [Required] public int? TransactionId { get; set; }
}

// Affordable: the fund stays at or above its minimum. BreachesMinimum: it would not. NoMinimum: the money is there, but the
// fund has no minimum in this currency to compare with. NotEnoughMoney: no minimum in this currency, and the fund would go below zero.
public enum AffordabilityVerdict { Affordable, BreachesMinimum, NoMinimum, NotEnoughMoney }

// Can it be afforded, from recorded balances:
// After = Available (the fund in this currency in bank and cash accounts) − CommittedElsewhere (its other committed purchases) − Purchase.
// Shortfall is how far After falls below the minimum, or below zero with no minimum. ObligationsDue is shown beside the answer
// and not deducted, because obligations are not tied to a fund.
public record AffordabilityDto(
    AffordabilityVerdict Verdict, MoneyDto Available, MoneyDto CommittedElsewhere, MoneyDto Purchase, MoneyDto After, MoneyDto? Minimum, MoneyDto? Shortfall,
    MoneyDto? MinimumInOtherCurrency, MoneyDto ObligationsDue);

public record PlannedPurchaseDto(
    int Id, string Name, MoneyDto Amount, int FundId, string FundName, string? Category, DateOnly? TargetDate, string? Notes, PlannedPurchaseStatus Status,
    int? TransactionId, MoneyDto? Paid, DateOnly? PurchasedOn, bool ExpenseReversed, AffordabilityDto? Affordability, DateTime CreatedAt, DateTime? UpdatedAt);

// What the business intends to buy. A purchase is recorded by linking an expense already in the ledger; nothing here posts or
// moves money. The affordability check warns and never blocks: the decision stays the owner's.
public class PlannedPurchaseService
{
    public const string ActiveNameIndex = "IX_PlannedPurchases_Name_Open";
    public const string TransactionIndex = "IX_PlannedPurchases_LedgerTransactionId";
    public const int ObligationDays = 30;
    private const int CandidateDays = 60;

    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public PlannedPurchaseService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);

    // Open ones first, soonest target first; purchased and cancelled ones only when asked for
    public async Task<IReadOnlyList<PlannedPurchaseDto>> ListAsync(bool includeClosed)
    {
        var all = await _repository.GetPlannedPurchasesAsync();
        var shown = all.Where(p => includeClosed || p.IsOpen)
            .OrderBy(p => p.IsOpen ? 0 : 1).ThenBy(p => p.TargetDate ?? DateOnly.MaxValue).ThenBy(p => p.Name)
            .ToList();
        return await DtosAsync(shown, all);
    }

    public async Task<PlannedPurchaseDto> GetAsync(int id)
    {
        var purchase = await PurchaseAsync(id);
        return (await DtosAsync([purchase], await _repository.GetPlannedPurchasesAsync()))[0];
    }

    public async Task<PlannedPurchaseDto> CreateAsync(SavePlannedPurchaseRequest request, int userId)
    {
        var purchase = new PlannedPurchase(request.Name, Amount(request), await FundAsync(request.FundId!.Value), request.Category, request.TargetDate, request.Notes, Now);
        await SaveWithAuditAsync(() => _repository.Add(purchase), () => Audit(userId, "purchase.proposed", purchase,
            $"Proposed '{purchase.Name}': {purchase.Currency.Code} {purchase.Amount:0.00}."));
        return await GetAsync(purchase.Id);
    }

    public async Task<PlannedPurchaseDto> UpdateAsync(int id, SavePlannedPurchaseRequest request, int userId)
    {
        var purchase = await PurchaseAsync(id);
        var before = Snapshot(purchase);
        purchase.Update(request.Name, Amount(request), await FundAsync(request.FundId!.Value), request.Category, request.TargetDate, request.Notes, Now);
        await SaveWithAuditAsync(() => { }, () => Audit(userId, "purchase.updated", purchase, $"Updated '{purchase.Name}'.", new { before, after = Snapshot(purchase) }));
        return await GetAsync(id);
    }

    public Task<PlannedPurchaseDto> CommitAsync(int id, int userId) =>
        ChangeAsync(id, userId, "purchase.committed", (p, now) => p.Commit(now), p => $"Committed to '{p.Name}'.");

    public Task<PlannedPurchaseDto> WithdrawCommitmentAsync(int id, int userId) =>
        ChangeAsync(id, userId, "purchase.commitment-withdrawn", (p, now) => p.WithdrawCommitment(now), p => $"Put '{p.Name}' back to proposed.");

    public Task<PlannedPurchaseDto> CancelAsync(int id, int userId) =>
        ChangeAsync(id, userId, "purchase.cancelled", (p, now) => p.Cancel(now), p => $"Cancelled '{p.Name}'.");

    public async Task<PlannedPurchaseDto> RecordPurchaseAsync(int id, RecordPurchaseRequest request, int userId)
    {
        var purchase = await PurchaseAsync(id);
        var transactionId = request.TransactionId!.Value;
        var expense = await _repository.GetTransactionAsync(transactionId) ?? throw new DomainException($"Transaction {transactionId} does not exist.");
        var reversed = (await _repository.GetReversalIdsAsync([transactionId])).ContainsKey(transactionId);
        if ((await _repository.GetLinkedPaymentTransactionIdsAsync([transactionId])).Count > 0)
            throw new DomainException($"Transaction {transactionId} is already recorded as the payment of an obligation.");
        purchase.RecordPurchase(expense, reversed, Now);
        await SaveWithAuditAsync(() => { }, () => Audit(userId, "purchase.bought", purchase,
            $"Recorded transaction {transactionId} as the purchase of '{purchase.Name}'.",
            new { transactionId, purchase.PaidAmount, purchase.PurchasedOn }));
        return await GetAsync(id);
    }

    public async Task<PlannedPurchaseDto> UndoPurchaseAsync(int id, int userId)
    {
        var purchase = await PurchaseAsync(id);
        var transactionId = purchase.LedgerTransactionId;
        purchase.UndoPurchase(Now);
        await SaveWithAuditAsync(() => { }, () => Audit(userId, "purchase.undone", purchase,
            $"Removed transaction {transactionId} as the purchase of '{purchase.Name}'; the expense stays in the ledger.", new { transactionId }));
        return await GetAsync(id);
    }

    // Recent expenses in the purchase's currency that are not reversed and not linked to a purchase or an obligation, newest first
    public async Task<IReadOnlyList<PaymentCandidateDto>> CandidatesAsync(int id)
    {
        var purchase = await PurchaseAsync(id);
        var created = DateOnly.FromDateTime(purchase.CreatedAt);
        var from = (created < Today ? created : Today).AddDays(-CandidateDays);
        var expenses = (await _repository.GetTransactionsAsync(new TransactionQuery(from, null, LedgerTransactionType.Expense, PageSize: 200))).Items
            .Where(t => t.Entries.All(e => e.Currency == purchase.Currency))
            .ToList();
        var ids = expenses.Select(t => t.Id).ToList();
        var reversed = await _repository.GetReversalIdsAsync(ids);
        var payments = await _repository.GetLinkedPaymentTransactionIdsAsync(ids);
        var purchases = await _repository.GetLinkedPurchaseTransactionIdsAsync(ids);
        return expenses
            .Where(t => !reversed.ContainsKey(t.Id) && !payments.Contains(t.Id) && !purchases.Contains(t.Id))
            .Select(t => new PaymentCandidateDto(t.Id, t.TransactionDate, t.Description, t.Category, new MoneyDto(-t.Entries.Sum(e => e.Amount), purchase.Currency.Code)))
            .ToList();
    }

    private async Task<PlannedPurchaseDto> ChangeAsync(int id, int userId, string action, Action<PlannedPurchase, DateTime> change, Func<PlannedPurchase, string> summary)
    {
        var purchase = await PurchaseAsync(id);
        change(purchase, Now);
        await SaveWithAuditAsync(() => { }, () => Audit(userId, action, purchase, summary(purchase)));
        return await GetAsync(id);
    }

    private async Task<List<PlannedPurchaseDto>> DtosAsync(List<PlannedPurchase> shown, List<PlannedPurchase> all)
    {
        var readModel = await FinanceReadModel.LoadAsync(_repository);
        var funds = readModel.Funds().ToDictionary(f => f.Id);
        var accounts = readModel.Accounts();
        var horizon = Today.AddDays(ObligationDays);
        var obligations = (await _repository.GetObligationsAsync()).Where(o => o.Status == ObligationStatus.Active && o.DueDate <= horizon).ToList();
        var reversed = await _repository.GetReversalIdsAsync(shown.Where(p => p.LedgerTransactionId is not null).Select(p => p.LedgerTransactionId!.Value));

        return shown.Select(p =>
        {
            var currency = p.Currency.Code;
            return new PlannedPurchaseDto(
                p.Id, p.Name, new MoneyDto(p.Amount, currency), p.FundId, funds.GetValueOrDefault(p.FundId)?.Name ?? $"Fund {p.FundId}", p.Category, p.TargetDate, p.Notes, p.Status,
                p.LedgerTransactionId, p.PaidAmount is { } paid ? new MoneyDto(paid, currency) : null, p.PurchasedOn,
                p.LedgerTransactionId is { } t && reversed.ContainsKey(t),
                p.IsOpen ? Check(p, funds.GetValueOrDefault(p.FundId), accounts, all, obligations) : null,
                p.CreatedAt, p.UpdatedAt);
        }).ToList();
    }

    private static AffordabilityDto Check(PlannedPurchase purchase, FundDto? fund, IReadOnlyList<AccountDto> accounts, List<PlannedPurchase> all, List<Obligation> obligations)
    {
        var currency = purchase.Currency.Code;
        MoneyDto M(decimal amount) => new(amount, currency);

        var available = accounts
            .Where(a => a.Type is FinancialAccountType.Bank or FinancialAccountType.Cash)
            .SelectMany(a => a.Funds)
            .Where(f => f.FundId == purchase.FundId && f.Balance.Currency == currency)
            .Sum(f => f.Balance.Amount);
        var committed = all
            .Where(p => p.Id != purchase.Id && p.Status == PlannedPurchaseStatus.Committed && p.FundId == purchase.FundId && p.Currency == purchase.Currency)
            .Sum(p => p.Amount);
        var after = available - committed - purchase.Amount;

        var minimum = fund?.Minimum?.Currency == currency ? fund.Minimum : null;
        var floor = minimum?.Amount ?? 0;
        var verdict = minimum is not null
            ? (after >= floor ? AffordabilityVerdict.Affordable : AffordabilityVerdict.BreachesMinimum)
            : (after >= 0 ? AffordabilityVerdict.NoMinimum : AffordabilityVerdict.NotEnoughMoney);

        return new AffordabilityDto(
            verdict, M(available), M(committed), M(purchase.Amount), M(after), minimum, after < floor ? M(floor - after) : null,
            fund?.Minimum is { } other && other.Currency != currency ? other : null,
            M(obligations.Where(o => o.Currency == purchase.Currency).Sum(o => o.Amount)));
    }

    private async Task<PlannedPurchase> PurchaseAsync(int id) =>
        await _repository.GetPlannedPurchaseAsync(id) ?? throw new NotFoundException($"Planned purchase {id} does not exist.");

    private async Task<Fund> FundAsync(int id) =>
        await _repository.GetFundAsync(id) ?? throw new DomainException($"Fund {id} does not exist.");

    private static Money Amount(SavePlannedPurchaseRequest request) => Money.Of(request.Amount!.Value, Currency.FromCode(request.Currency));

    private static object Snapshot(PlannedPurchase p) => new { p.Name, p.Amount, Currency = p.Currency.Code, p.FundId, p.Category, p.TargetDate, p.Notes, p.Status };

    private AuditEvent Audit(int userId, string action, PlannedPurchase p, string summary, object? data = null) =>
        AuditLog.Event(Now, userId, action, nameof(PlannedPurchase), p.Id, summary, data ?? Snapshot(p));

    // Saves the change, then the audit event that references its id, atomically
    private async Task SaveWithAuditAsync(Action change, Func<AuditEvent> audit)
    {
        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                change();
                await _repository.SaveChangesAsync();
                _repository.Add(audit());
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == ActiveNameIndex)
        {
            throw new ConflictException("An open planned purchase already has that name.");
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == TransactionIndex)
        {
            throw new ConflictException("That expense is already recorded as another purchase.");
        }
    }
}
