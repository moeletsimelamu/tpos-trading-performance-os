using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

public class SaveObligationRequest
{
    [Required, MaxLength(Obligation.MaxNameLength)] public string Name { get; set; } = string.Empty;
    [Required] public decimal? Amount { get; set; }
    [Required] public string Currency { get; set; } = string.Empty;
    [MaxLength(Obligation.MaxCategoryLength)] public string? Category { get; set; }
    [Required] public ObligationRecurrence? Recurrence { get; set; }
    [Required] public DateOnly? FirstDueDate { get; set; }
    [MaxLength(Obligation.MaxNotesLength)] public string? Notes { get; set; }
}

public class RecordObligationPaymentRequest
{
    [Required] public int? TransactionId { get; set; }
}

// Reversed: the expense was reversed after it was linked, so this payment no longer stands
public record ObligationPaymentDto(int Id, int TransactionId, DateOnly DueDate, DateOnly PaidOn, MoneyDto Amount, bool Reversed);

public record ObligationDto(
    int Id, string Name, MoneyDto Amount, string? Category, ObligationRecurrence Recurrence, DateOnly FirstDueDate,
    DateOnly DueDate, ObligationStatus Status, ObligationStanding Standing, int DaysUntilDue, string? Notes,
    IReadOnlyList<ObligationPaymentDto> Payments, DateTime CreatedAt, DateTime? UpdatedAt);

// An expense that could pay an obligation: same currency, not reversed, not already linked to a payment
public record PaymentCandidateDto(int TransactionId, DateOnly Date, string Description, string? Category, MoneyDto Amount);

// What the business has to pay, and when. Payments are links to expenses already in the ledger; nothing here posts
// or moves money. Every change is audited like other financial records.
public class ObligationService
{
    public const string ActiveNameIndex = "IX_Obligations_Name_Active";
    public const string PaymentTransactionIndex = "IX_ObligationPayments_LedgerTransactionId";
    private const int CandidateDays = 60;

    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public ObligationService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);

    // Soonest due first; completed and cancelled ones only when asked for
    public async Task<IReadOnlyList<ObligationDto>> ListAsync(bool includeClosed)
    {
        var obligations = (await _repository.GetObligationsAsync())
            .Where(o => includeClosed || o.Status == ObligationStatus.Active)
            .OrderBy(o => o.Status).ThenBy(o => o.DueDate).ThenBy(o => o.Name)
            .ToList();
        return await DtosAsync(obligations);
    }

    public async Task<ObligationDto> GetAsync(int id) => (await DtosAsync([await ObligationAsync(id)]))[0];

    public async Task<ObligationDto> CreateAsync(SaveObligationRequest request, int userId)
    {
        var obligation = new Obligation(request.Name, Amount(request), request.Category, request.Recurrence!.Value, request.FirstDueDate!.Value, request.Notes, Now);
        await SaveWithAuditAsync(() => _repository.Add(obligation), () => Audit(userId, "obligation.created", obligation,
            $"Added obligation '{obligation.Name}': {obligation.Currency.Code} {obligation.Amount:0.00}, {obligation.Recurrence}, first due {obligation.FirstDueDate:yyyy-MM-dd}."));
        return await GetAsync(obligation.Id);
    }

    public async Task<ObligationDto> UpdateAsync(int id, SaveObligationRequest request, int userId)
    {
        var obligation = await ObligationAsync(id);
        var before = Snapshot(obligation);
        obligation.Update(request.Name, Amount(request), request.Category, request.Recurrence!.Value, request.FirstDueDate!.Value, request.Notes, Now);
        await SaveWithAuditAsync(() => { }, () => Audit(userId, "obligation.updated", obligation, $"Updated obligation '{obligation.Name}'.",
            new { before, after = Snapshot(obligation) }));
        return await GetAsync(id);
    }

    public async Task<ObligationDto> CancelAsync(int id, int userId)
    {
        var obligation = await ObligationAsync(id);
        obligation.Cancel(Now);
        await SaveWithAuditAsync(() => { }, () => Audit(userId, "obligation.cancelled", obligation, $"Cancelled obligation '{obligation.Name}'."));
        return await GetAsync(id);
    }

    public async Task<ObligationDto> RecordPaymentAsync(int id, RecordObligationPaymentRequest request, int userId)
    {
        var obligation = await ObligationAsync(id);
        var transactionId = request.TransactionId!.Value;
        var expense = await _repository.GetTransactionAsync(transactionId) ?? throw new DomainException($"Transaction {transactionId} does not exist.");
        var reversed = (await _repository.GetReversalIdsAsync([transactionId])).ContainsKey(transactionId);
        if ((await _repository.GetLinkedPurchaseTransactionIdsAsync([transactionId])).Count > 0)
            throw new DomainException($"Transaction {transactionId} is already recorded as a planned purchase.");
        var payment = obligation.RecordPayment(expense, reversed, Now);
        await SaveWithAuditAsync(() => { }, () => Audit(userId, "obligation.paid", obligation,
            $"Recorded transaction {transactionId} as the payment of '{obligation.Name}' due {payment.DueDate:yyyy-MM-dd}.",
            new { transactionId, payment.DueDate, payment.PaidOn, payment.Amount }));
        return await GetAsync(id);
    }

    public async Task<ObligationDto> UndoLastPaymentAsync(int id, int userId)
    {
        var obligation = await ObligationAsync(id);
        var payment = obligation.UndoLastPayment(Now);
        // The payment left the obligation's collection, so it is deleted with the save
        await SaveWithAuditAsync(() => { }, () => Audit(userId, "obligation.payment-undone", obligation,
            $"Removed transaction {payment.LedgerTransactionId} as the payment of '{obligation.Name}' due {payment.DueDate:yyyy-MM-dd}; the expense stays in the ledger.",
            new { transactionId = payment.LedgerTransactionId, payment.DueDate }));
        return await GetAsync(id);
    }

    // Recent expenses in the obligation's currency that are not reversed and not already a payment, newest first
    public async Task<IReadOnlyList<PaymentCandidateDto>> CandidatesAsync(int id)
    {
        var obligation = await ObligationAsync(id);
        var from = (obligation.DueDate < Today ? obligation.DueDate : Today).AddDays(-CandidateDays);
        var expenses = (await _repository.GetTransactionsAsync(new TransactionQuery(from, null, LedgerTransactionType.Expense, PageSize: 200))).Items
            .Where(t => t.Entries.All(e => e.Currency == obligation.Currency))
            .ToList();
        var reversed = await _repository.GetReversalIdsAsync(expenses.Select(t => t.Id));
        var linked = await _repository.GetLinkedPaymentTransactionIdsAsync(expenses.Select(t => t.Id));
        linked.UnionWith(await _repository.GetLinkedPurchaseTransactionIdsAsync(expenses.Select(t => t.Id)));
        return expenses
            .Where(t => !reversed.ContainsKey(t.Id) && !linked.Contains(t.Id))
            .Select(t => new PaymentCandidateDto(t.Id, t.TransactionDate, t.Description, t.Category, new MoneyDto(-t.Entries.Sum(e => e.Amount), obligation.Currency.Code)))
            .ToList();
    }

    private async Task<List<ObligationDto>> DtosAsync(List<Obligation> obligations)
    {
        var reversed = await _repository.GetReversalIdsAsync(obligations.SelectMany(o => o.Payments).Select(p => p.LedgerTransactionId));
        var today = Today;
        return obligations.Select(o => new ObligationDto(
            o.Id, o.Name, new MoneyDto(o.Amount, o.Currency.Code), o.Category, o.Recurrence, o.FirstDueDate, o.DueDate, o.Status,
            o.StandingOn(today), o.DueDate.DayNumber - today.DayNumber, o.Notes,
            o.Payments.Select(p => new ObligationPaymentDto(p.Id, p.LedgerTransactionId, p.DueDate, p.PaidOn, new MoneyDto(p.Amount, o.Currency.Code), reversed.ContainsKey(p.LedgerTransactionId))).ToList(),
            o.CreatedAt, o.UpdatedAt)).ToList();
    }

    private async Task<Obligation> ObligationAsync(int id) =>
        await _repository.GetObligationAsync(id) ?? throw new NotFoundException($"Obligation {id} does not exist.");

    private static Money Amount(SaveObligationRequest request) => Money.Of(request.Amount!.Value, Currency.FromCode(request.Currency));

    private static object Snapshot(Obligation o) => new { o.Name, o.Amount, Currency = o.Currency.Code, o.Category, o.Recurrence, o.FirstDueDate, o.Notes };

    private AuditEvent Audit(int userId, string action, Obligation o, string summary, object? data = null) =>
        AuditLog.Event(Now, userId, action, nameof(Obligation), o.Id, summary, data ?? Snapshot(o));

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
            throw new ConflictException("An active obligation already has that name.");
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == PaymentTransactionIndex)
        {
            throw new ConflictException("That expense is already recorded as the payment of an obligation.");
        }
    }
}
