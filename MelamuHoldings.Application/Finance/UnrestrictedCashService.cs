using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

public record UnrestrictedHoldingDto(int AccountId, string AccountName, int FundId, string FundName, MoneyDto Amount);

// One currency's working (Domain: UnrestrictedCash). ObligationsDue is shown beside the figure and not deducted.
public record UnrestrictedCurrencyDto(
    string Currency, decimal Held, decimal OperatingMinimum, decimal OwedToOtherFunds, decimal PayoutsNotAllocated, decimal CommittedPurchases,
    decimal Amount, decimal ObligationsDue, decimal AfterObligations, IReadOnlyList<UnrestrictedHoldingDto> Holdings);

public record UnrestrictedCashDto(IReadOnlyList<UnrestrictedCurrencyDto> Currencies, IReadOnlyList<NotCountedDto> NotCounted);

// How much cash is free to use, from records only (PLAN.md D9). A claim comes off only when its money is in a counted account:
// a payout received into a personal account was never in the figure, so there is nothing to take off.
public class UnrestrictedCashService
{
    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public UnrestrictedCashService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<UnrestrictedCashDto> GetAsync()
    {
        var readModel = await FinanceReadModel.LoadAsync(_repository);
        var accounts = readModel.Accounts();
        var counted = accounts.Where(a => UnrestrictedCash.CountsAccount(a.Type, a.Ownership)).ToList();
        var countedIds = counted.Select(a => a.Id).ToHashSet();

        var holdings = counted
            .SelectMany(a => a.Funds.Where(f => UnrestrictedCash.CountsFund(f.FundId))
                .Select(f => new UnrestrictedHoldingDto(a.Id, a.Name, f.FundId, f.FundName, f.Balance)))
            .ToList();
        var minimum = readModel.Funds().Single(f => f.Id == Fund.OperatingCapitalId).Minimum;

        var allocations = (await _repository.GetAllocationsAsync()).Where(a => !a.IsCancelled).ToList();
        var settlements = allocations.SelectMany(a => a.Lines).SelectMany(l => l.Settlements).Select(s => s.LedgerTransactionId).ToList();
        var reversed = settlements.Count == 0 ? new HashSet<int>() : (await _repository.GetReversalIdsAsync(settlements)).Keys.ToHashSet();
        // A line for a counted fund stays unrestricted once settled, so only other funds' lines come off
        var owed = allocations.Where(a => countedIds.Contains(a.SourceAccountId))
            .SelectMany(a => a.Lines)
            .Where(l => !UnrestrictedCash.CountsFund(l.FundId))
            .Select(l => l.Outstanding(reversed))
            .ToLookup(m => m.Currency.Code, m => m.Amount);

        var allocatedPayouts = allocations.Select(a => a.PayoutId).ToHashSet();
        var notAllocated = (await _repository.GetPayoutsAsync(PayoutStatus.Received))
            .Where(p => !allocatedPayouts.Contains(p.Id) && p.ReceivedIntoAccountId is { } account && countedIds.Contains(account))
            .ToLookup(p => p.CreditedCurrency!.Code, p => p.CreditedAmount!.Value);

        var committed = (await _repository.GetPlannedPurchasesAsync())
            .Where(p => p.Status == PlannedPurchaseStatus.Committed && UnrestrictedCash.CountsFund(p.FundId))
            .ToLookup(p => p.Currency.Code, p => p.Amount);

        var horizon = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(PlannedPurchaseService.ObligationDays);
        var obligations = (await _repository.GetObligationsAsync())
            .Where(o => o.Status == ObligationStatus.Active && o.DueDate <= horizon)
            .ToLookup(o => o.Currency.Code, o => o.Amount);

        var currencies = holdings.Select(h => h.Amount.Currency)
            .Concat(minimum is null ? [] : [minimum.Currency])
            .Concat(owed.Where(g => g.Sum() != 0).Select(g => g.Key))
            .Concat(notAllocated.Select(g => g.Key))
            .Concat(committed.Select(g => g.Key))
            .Concat(obligations.Select(g => g.Key))
            .Distinct().OrderBy(c => c);

        return new UnrestrictedCashDto(
            currencies.Select(c =>
            {
                var mine = holdings.Where(h => h.Amount.Currency == c).ToList();
                var cash = new UnrestrictedCash(
                    mine.Sum(h => h.Amount.Amount), minimum?.Currency == c ? minimum.Amount : 0, owed[c].Sum(), notAllocated[c].Sum(), committed[c].Sum());
                var due = obligations[c].Sum();
                return new UnrestrictedCurrencyDto(c, cash.Held, cash.OperatingMinimum, cash.OwedToOtherFunds, cash.PayoutsNotAllocated, cash.CommittedPurchases,
                    cash.Amount, due, cash.AfterObligations(due), mine);
            }).ToList(),
            NotCounted(accounts));
    }

    // The two funds' money outside the counted accounts, and why
    private static List<NotCountedDto> NotCounted(IReadOnlyList<AccountDto> accounts) => accounts
        .Where(a => !UnrestrictedCash.CountsAccount(a.Type, a.Ownership))
        .SelectMany(a => a.Funds.Where(f => UnrestrictedCash.CountsFund(f.FundId))
            .Select(f => new NotCountedDto(Reason(a), f.Balance, a.Name)))
        .ToList();

    private static string Reason(AccountDto account) => account switch
    {
        { Type: not (FinancialAccountType.Bank or FinancialAccountType.Cash) } => $"In a {account.Type.ToString().ToLowerInvariant()} account, not bank or cash",
        { Ownership: Ownership.Personal } => "In a personal account",
        _ => "The account's ownership is not stated"
    };
}
