using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

public class RecordValuationRequest
{
    [Required] public DateOnly? ValuedOn { get; set; }
    [Required] public decimal? Value { get; set; }
    [MaxLength(InvestmentValuation.MaxNoteLength)] public string? Note { get; set; }
}

// A valuation with what the ledger recorded for the account up to its date, and the unrealised gain or loss between them
public record ValuationDto(
    int Id, int AccountId, DateOnly ValuedOn, MoneyDto Value, string? Note, DateTime RecordedAt, MoneyDto RecordedBalance, MoneyDto Unrealised);

// One brokerage account, as of its latest valuation (or today when it has none):
// - NetContributions: opening balances, transfers and exchanges in, less those out (money the business put in);
// - RecordedIncome and RecordedCosts: income, expenses and fees recorded in the account (dividends, broker fees);
// - RecordedBalance = NetContributions + RecordedIncome + RecordedCosts, the ledger balance on that date;
// - Unrealised = value − RecordedBalance: the change in market value the ledger does not hold. Not cash, not income.
// - GainOnContributions = value − NetContributions: unrealised plus recorded income and costs.
// Movements dated after the valuation are counted apart (MovedSinceValuation), so a later contribution is never
// mistaken for a gain or loss.
public record InvestmentAccountDto(
    int AccountId, string AccountName, string Currency, string? Institution, Ownership Ownership, FinancialAccountStatus Status,
    DateOnly AsOf, ValuationDto? Latest, MoneyDto NetContributions, MoneyDto RecordedIncome, MoneyDto RecordedCosts, MoneyDto RecordedBalance,
    MoneyDto? Unrealised, MoneyDto? GainOnContributions, int TransactionsSinceValuation, MoneyDto MovedSinceValuation, MoneyDto CurrentRecordedBalance,
    TaxTreatment TaxTreatment);

// Accounts with a valuation, per currency; never added across currencies. Valuations may be of different dates.
public record InvestmentTotalDto(string Currency, decimal Value, decimal NetContributions, decimal Unrealised, int Accounts);

public record InvestmentsDto(IReadOnlyList<InvestmentAccountDto> Accounts, IReadOnlyList<InvestmentTotalDto> Totals);

// Investments (Phase 7.3): brokerage accounts, the valuations the owner records for them, and the unrealised gain or loss
// against the ledger. A valuation never posts to the ledger; contributions and withdrawals are the ledger's own transfers,
// exchanges and opening balances.
public class InvestmentService
{
    public const string ValuationDateIndex = "IX_InvestmentValuations_FinancialAccountId_ValuedOn";

    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public InvestmentService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<InvestmentsDto> GetAsync()
    {
        var accounts = (await _repository.GetAccountsAsync()).Where(a => a.Type == FinancialAccountType.Brokerage).OrderBy(a => a.Name).ToList();
        var entries = (await _repository.GetBrokerageEntriesAsync()).ToLookup(e => e.AccountId);
        var latest = (await _repository.GetValuationsAsync())
            .GroupBy(v => v.FinancialAccountId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.ValuedOn).First());
        var today = DateOnly.FromDateTime(Now);

        var dtos = accounts.Select(a => Account(a, entries[a.Id].ToList(), latest.GetValueOrDefault(a.Id), today)).ToList();
        var totals = dtos.Where(d => d.Latest is not null)
            .GroupBy(d => d.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new InvestmentTotalDto(g.Key, g.Sum(d => d.Latest!.Value.Amount), g.Sum(d => d.NetContributions.Amount), g.Sum(d => d.Unrealised!.Amount), g.Count()))
            .ToList();
        return new InvestmentsDto(dtos, totals);
    }

    // Newest first, each compared with the ledger on its own date
    public async Task<IReadOnlyList<ValuationDto>> ListValuationsAsync(int accountId)
    {
        var account = await BrokerageAsync(accountId);
        var entries = (await _repository.GetBrokerageEntriesAsync()).Where(e => e.AccountId == account.Id).ToList();
        return (await _repository.GetValuationsAsync(account.Id))
            .OrderByDescending(v => v.ValuedOn)
            .Select(v => Dto(v, entries))
            .ToList();
    }

    public async Task<ValuationDto> RecordAsync(int accountId, RecordValuationRequest request, int userId)
    {
        var account = await BrokerageAsync(accountId);
        var valuation = new InvestmentValuation(account, request.ValuedOn!.Value, request.Value!.Value, request.Note, userId, Now);
        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                _repository.Add(valuation);
                await _repository.SaveChangesAsync();
                _repository.Add(AuditLog.Event(Now, userId, "valuation.recorded", nameof(InvestmentValuation), valuation.Id,
                    $"Valued '{account.Name}' at {valuation.Currency.Code} {AuditLog.Amount(valuation.Value)} on {valuation.ValuedOn:yyyy-MM-dd}.", Snapshot(valuation)));
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == ValuationDateIndex)
        {
            throw new ConflictException($"'{account.Name}' already has a valuation on {valuation.ValuedOn:yyyy-MM-dd}. Delete it first to replace it.");
        }
        var entries = (await _repository.GetBrokerageEntriesAsync()).Where(e => e.AccountId == account.Id).ToList();
        return Dto(valuation, entries);
    }

    // A valuation is an observation, not a ledger record, so a wrong one is deleted (and the deletion audited)
    public async Task DeleteAsync(int accountId, int valuationId, int userId)
    {
        var valuation = await _repository.GetValuationAsync(valuationId);
        if (valuation is null || valuation.FinancialAccountId != accountId)
            throw new NotFoundException($"Valuation {valuationId} of account {accountId} does not exist.");
        await _repository.InTransactionAsync(async () =>
        {
            _repository.Remove(valuation);
            _repository.Add(AuditLog.Event(Now, userId, "valuation.deleted", nameof(InvestmentValuation), valuation.Id,
                $"Deleted the {valuation.ValuedOn:yyyy-MM-dd} valuation of account {accountId} ({valuation.Currency.Code} {AuditLog.Amount(valuation.Value)}).", Snapshot(valuation)));
            await _repository.SaveChangesAsync();
            return true;
        });
    }

    private static InvestmentAccountDto Account(FinancialAccount account, List<InvestmentEntryRow> entries, InvestmentValuation? latest, DateOnly today)
    {
        var currency = account.Currency.Code;
        var asOf = latest?.ValuedOn ?? today;
        var upTo = entries.Where(e => e.Date <= asOf).ToList();
        var after = entries.Where(e => e.Date > asOf).ToList();
        var (contributions, income, costs) = Split(upTo);
        var recorded = upTo.Sum(e => e.Amount);
        MoneyDto M(decimal amount) => new(amount, currency);

        return new InvestmentAccountDto(
            account.Id, account.Name, currency, account.Institution, account.Ownership, account.Status, asOf,
            latest is null ? null : Dto(latest, entries),
            M(contributions), M(income), M(costs), M(recorded),
            latest is null ? null : M(latest.Unrealised(recorded)),
            latest is null ? null : M(latest.GainOnContributions(contributions)),
            latest is null ? 0 : after.Select(e => e.TransactionId).Distinct().Count(),
            M(latest is null ? 0 : after.Sum(e => e.Amount)),
            M(entries.Sum(e => e.Amount)),
            account.TaxTreatment);
    }

    private static ValuationDto Dto(InvestmentValuation v, List<InvestmentEntryRow> entries)
    {
        var recorded = entries.Where(e => e.Date <= v.ValuedOn).Sum(e => e.Amount);
        var currency = v.Currency.Code;
        return new ValuationDto(v.Id, v.FinancialAccountId, v.ValuedOn, new MoneyDto(v.Value, currency), v.Note, v.RecordedAt,
            new MoneyDto(recorded, currency), new MoneyDto(v.Unrealised(recorded), currency));
    }

    // Each entry falls in one group (Domain: InvestmentValuation.FlowOf); earmarks net to zero inside the account
    private static (decimal Contributions, decimal Income, decimal Costs) Split(List<InvestmentEntryRow> entries)
    {
        decimal Sum(InvestmentFlow flow) => entries.Where(e => InvestmentValuation.FlowOf(e.Type, e.Role) == flow).Sum(e => e.Amount);
        return (Sum(InvestmentFlow.Contribution), Sum(InvestmentFlow.Income), Sum(InvestmentFlow.Cost));
    }

    private async Task<FinancialAccount> BrokerageAsync(int accountId)
    {
        var account = await _repository.GetAccountAsync(accountId) ?? throw new NotFoundException($"Account {accountId} does not exist.");
        if (account.Type != FinancialAccountType.Brokerage)
            throw new DomainException($"'{account.Name}' is a {account.Type.ToString().ToLowerInvariant()} account; valuations are for brokerage accounts.");
        return account;
    }

    private static object Snapshot(InvestmentValuation v) =>
        new { v.FinancialAccountId, v.ValuedOn, v.Value, Currency = v.Currency.Code, v.Note };
}
