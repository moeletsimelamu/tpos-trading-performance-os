using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

public class UpdateContinuitySettingsRequest
{
    [Required] public int? ContinuityFundId { get; set; }
    [Required, Range(1, ContinuitySettings.MaxMonths)] public int? Months { get; set; }
    public decimal? MonthlyEssentialExpense { get; set; }
    public int? RestorationFundId { get; set; }
}

public record ContinuitySettingsDto(
    int ContinuityFundId, string ContinuityFundName, int Months, string Currency, decimal? MonthlyEssentialExpense,
    int? RestorationFundId, string? RestorationFundName, DateTime? UpdatedAt);

// Counted continuity money in one bank or cash account. Separated: the account holds no other fund's money.
public record ContinuityHoldingDto(int AccountId, string AccountName, MoneyDto Amount, bool Separated);

// Money of the fund that is deliberately not counted, and why
public record NotCountedDto(string Reason, MoneyDto Amount, string? AccountName = null);

public record RestorationDto(
    int FundId, string FundName, MoneyDto? Target, MoneyDto? Cash, MoneyDto? Shortfall, decimal? Progress, IReadOnlyList<NotCountedDto> NotCounted);

// CoverageMonths is rounded down, so it never overstates. Target, coverage and shortfall are null until the monthly
// essential expense is entered.
public record ContinuityReportDto(
    ContinuitySettingsDto Settings, MoneyDto Cash, MoneyDto? Target, decimal? CoverageMonths, MoneyDto? Shortfall, bool BelowTarget,
    MoneyDto Earmarked, MoneyDto Separated, IReadOnlyList<ContinuityHoldingDto> Holdings, IReadOnlyList<NotCountedDto> NotCounted,
    RestorationDto? Restoration);

// How close continuity is to its target, from recorded balances only (PLAN.md §1a and D6–D8):
// - continuity cash is the continuity fund's balance in the target currency in Bank and Cash accounts;
// - money in another currency, in brokerage or other accounts, or allocated but not yet earmarked is shown, never counted;
// - the restoration capital is a different fund, measured against that fund's own target, so nothing counts twice.
public class ContinuityService
{
    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public ContinuityService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<ContinuityReportDto> GetReportAsync()
    {
        var settings = await SettingsAsync();
        var readModel = await FinanceReadModel.LoadAsync(_repository);
        var funds = readModel.Funds().ToDictionary(f => f.Id);
        var accounts = readModel.Accounts();
        var outstanding = await OutstandingByFundAsync();
        var currency = settings.Currency.Code;

        var (cash, holdings, notCounted) = Measure(settings.ContinuityFundId, currency, accounts, outstanding);
        var target = settings.Target;
        var coverage = settings.MonthlyEssentialExpense is { } expense
            ? Math.Max(0, Math.Round(cash / expense, 1, MidpointRounding.ToZero))
            : (decimal?)null;

        RestorationDto? restoration = null;
        if (settings.RestorationFundId is { } restorationId && funds.TryGetValue(restorationId, out var fund))
        {
            // Measured in the currency of the fund's target; without one there is nothing to measure against yet
            var restorationCurrency = fund.Target?.Currency;
            if (restorationCurrency is null)
                restoration = new RestorationDto(fund.Id, fund.Name, null, null, null, null, []);
            else
            {
                var (restorationCash, _, restorationNotCounted) = Measure(fund.Id, restorationCurrency, accounts, outstanding);
                var restorationTarget = fund.Target!.Amount;
                restoration = new RestorationDto(fund.Id, fund.Name, fund.Target, new MoneyDto(restorationCash, restorationCurrency),
                    new MoneyDto(Math.Max(0, restorationTarget - restorationCash), restorationCurrency),
                    Math.Clamp(Math.Round(restorationCash / restorationTarget * 100, 1, MidpointRounding.ToZero), 0, 100), restorationNotCounted);
            }
        }

        return new ContinuityReportDto(
            ToDto(settings, funds), new MoneyDto(cash, currency),
            target is { } t ? new MoneyDto(t, currency) : null, coverage,
            target is { } goal ? new MoneyDto(Math.Max(0, goal - cash), currency) : null,
            target is { } limit && cash < limit,
            new MoneyDto(holdings.Where(h => !h.Separated).Sum(h => h.Amount.Amount), currency),
            new MoneyDto(holdings.Where(h => h.Separated).Sum(h => h.Amount.Amount), currency),
            holdings, notCounted, restoration);
    }

    public async Task<ContinuitySettingsDto> UpdateSettingsAsync(UpdateContinuitySettingsRequest request, int userId)
    {
        var settings = await SettingsAsync();
        var continuityFund = await _repository.GetFundAsync(request.ContinuityFundId!.Value)
            ?? throw new DomainException($"Fund {request.ContinuityFundId} does not exist.");
        var restorationFund = request.RestorationFundId is { } id
            ? await _repository.GetFundAsync(id) ?? throw new DomainException($"Fund {id} does not exist.")
            : null;

        var before = Snapshot(settings);
        settings.Update(continuityFund, request.Months!.Value, request.MonthlyEssentialExpense, restorationFund, Now);
        await _repository.InTransactionAsync(async () =>
        {
            await _repository.SaveChangesAsync();
            _repository.Add(AuditLog.Event(Now, userId, "continuity.updated", nameof(ContinuitySettings), settings.Id,
                $"Updated continuity: {settings.Months} months of "
                + (settings.MonthlyEssentialExpense is { } expense ? $"{settings.Currency.Code} {expense:0.00}" : "an essential expense not yet entered")
                + $" from '{continuityFund.Name}'"
                + (restorationFund is null ? "; no restoration fund." : $"; restoration from '{restorationFund.Name}'."),
                new { before, after = Snapshot(settings) }));
            await _repository.SaveChangesAsync();
            return true;
        });
        return ToDto(settings, (await FinanceReadModel.LoadAsync(_repository)).Funds().ToDictionary(f => f.Id));
    }

    // The fund's balance in the currency, in Bank and Cash accounts; everything else of the fund is listed, not counted
    private static (decimal Cash, List<ContinuityHoldingDto> Holdings, List<NotCountedDto> NotCounted) Measure(
        int fundId, string currency, IReadOnlyList<AccountDto> accounts, Dictionary<int, List<MoneyDto>> outstanding)
    {
        var holdings = new List<ContinuityHoldingDto>();
        var notCounted = new List<NotCountedDto>();
        foreach (var account in accounts)
        {
            var position = account.Funds.FirstOrDefault(f => f.FundId == fundId && f.Balance.Amount != 0);
            if (position is null) continue;
            var cashAccount = account.Type is FinancialAccountType.Bank or FinancialAccountType.Cash;
            if (position.Balance.Currency != currency)
                notCounted.Add(new NotCountedDto($"In {position.Balance.Currency}, not {currency}; never converted", position.Balance, account.Name));
            else if (!cashAccount)
                notCounted.Add(new NotCountedDto($"In a {account.Type.ToString().ToLowerInvariant()} account, not bank or cash", position.Balance, account.Name));
            else
                holdings.Add(new ContinuityHoldingDto(account.Id, account.Name, position.Balance,
                    Separated: account.Funds.Where(f => f.Balance.Amount != 0).All(f => f.FundId == fundId)));
        }
        foreach (var amount in outstanding.GetValueOrDefault(fundId) ?? [])
            notCounted.Add(new NotCountedDto("Allocated but not yet earmarked or transferred", amount));
        return (holdings.Sum(h => h.Amount.Amount), holdings, notCounted);
    }

    // What confirmed allocations still owe each fund, per currency (settlements whose transaction was reversed do not count)
    private async Task<Dictionary<int, List<MoneyDto>>> OutstandingByFundAsync()
    {
        // Cancelled allocations owe nothing; of the rest, only lines with something outstanding remain below
        var open = (await _repository.GetAllocationsAsync()).Where(a => !a.IsCancelled).ToList();
        var settlementIds = open.SelectMany(a => a.Lines).SelectMany(l => l.Settlements).Select(s => s.LedgerTransactionId).ToList();
        var reversed = settlementIds.Count == 0 ? new HashSet<int>() : (await _repository.GetReversalIdsAsync(settlementIds)).Keys.ToHashSet();
        return open.SelectMany(a => a.Lines)
            .Select(l => (l.FundId, Outstanding: l.Outstanding(reversed)))
            .Where(x => !x.Outstanding.IsZero)
            .GroupBy(x => x.FundId)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.Outstanding.Currency.Code)
                .Select(c => new MoneyDto(c.Sum(x => x.Outstanding.Amount), c.Key)).OrderBy(m => m.Currency).ToList());
    }

    private async Task<ContinuitySettings> SettingsAsync() =>
        await _repository.GetContinuitySettingsAsync() ?? throw new InvalidOperationException("The continuity settings record is missing; run the migrations.");

    private static ContinuitySettingsDto ToDto(ContinuitySettings s, Dictionary<int, FundDto> funds) => new(
        s.ContinuityFundId, funds.GetValueOrDefault(s.ContinuityFundId)?.Name ?? $"Fund {s.ContinuityFundId}", s.Months, s.Currency.Code,
        s.MonthlyEssentialExpense, s.RestorationFundId, s.RestorationFundId is { } id ? funds.GetValueOrDefault(id)?.Name : null, s.UpdatedAt);

    private static object Snapshot(ContinuitySettings s) =>
        new { s.ContinuityFundId, s.Months, Currency = s.Currency.Code, s.MonthlyEssentialExpense, s.RestorationFundId };
}
