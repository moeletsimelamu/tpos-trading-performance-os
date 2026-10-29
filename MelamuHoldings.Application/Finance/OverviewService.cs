using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// One kind of movement in bank and cash accounts over the period. Type is the ledger filter that lists exactly these
// transactions; Gross and Fees are set for income, where a fee deducted on the way is part of the same transaction.
public record CashLineDto(LedgerTransactionType Type, string Label, decimal Amount, int Transactions, decimal? Gross = null, decimal? Fees = null);

// Cash in one currency over the period: where it started, what moved it, and where it ended.
// CashAtEnd = CashAtStart + NetCashMovement + OpeningBalances.Amount, always.
public record CurrencyPeriodDto(
    string Currency, decimal CashAtStart, IReadOnlyList<CashLineDto> Movements, decimal NetCashMovement,
    CashLineDto OpeningBalances, decimal CashAtEnd);

// One calendar month (clipped to the period) in one currency
public record MonthMovementDto(DateOnly Start, DateOnly End, string Currency, decimal Income, decimal Expenses, decimal NetCashMovement, int Transactions);

public record PayoutsReceivedDto(string Currency, decimal Credited, int Payouts);

public record OverviewDto(
    DateOnly From, DateOnly To, IReadOnlyList<CurrencyPeriodDto> Currencies, IReadOnlyList<MonthMovementDto> Months,
    bool MonthsTruncated, IReadOnlyList<PayoutsReceivedDto> PayoutsReceived);

// The Executive Overview's figures for a period. Everything is summed from ledger entries in Bank and Cash accounts
// (the same scope as the cash totals), grouped by transaction type, so each figure is exactly the list of transactions
// the ledger shows for that type, those dates and bank-and-cash accounts. Net cash movement is cash movement, not profit:
// it is what entered and left those accounts, whatever the reason.
public class OverviewService
{
    public const int MaxYears = 10;
    public const int MaxMonths = 24;

    private static readonly (LedgerTransactionType Type, string Label)[] MovementTypes =
    [
        (LedgerTransactionType.Income, "Income received"),
        (LedgerTransactionType.Expense, "Expenses"),
        (LedgerTransactionType.CurrencyExchange, "Currency exchanges"),
        (LedgerTransactionType.Transfer, "Transfers"),   // between two bank or cash accounts they cancel out
        (LedgerTransactionType.Reversal, "Reversals (corrections)")
    ];

    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public OverviewService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    // This month to date unless a period is given
    public async Task<OverviewDto> GetAsync(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (start > end) throw new DomainException("The period must start on or before its end.");
        if (start < end.AddYears(-MaxYears)) throw new DomainException($"Choose a period of at most {MaxYears} years.");

        var rows = await _repository.GetCashEntriesAsync(end);
        var before = rows.Where(r => r.Date < start).ToList();
        var during = rows.Where(r => r.Date >= start).ToList();

        var currencies = rows.Select(r => r.Currency).Distinct().OrderBy(c => c).ToList();
        var periods = currencies.Select(c => Period(c, before, during)).ToList();
        var (months, truncated) = Months(start, end, currencies, during);

        var payouts = (await _repository.GetPayoutsAsync(PayoutStatus.Received))
            .Where(p => p.ReceivedOn >= start && p.ReceivedOn <= end && p.CreditedCurrency is not null)
            .GroupBy(p => p.CreditedCurrency!.Code)
            .OrderBy(g => g.Key)
            .Select(g => new PayoutsReceivedDto(g.Key, g.Sum(p => p.CreditedAmount ?? 0), g.Count()))
            .ToList();

        return new OverviewDto(start, end, periods, months, truncated, payouts);
    }

    private static CurrencyPeriodDto Period(string currency, List<CashEntryRow> before, List<CashEntryRow> during)
    {
        var mine = during.Where(r => r.Currency == currency).ToList();
        CashLineDto Line(LedgerTransactionType type, string label)
        {
            var ofType = mine.Where(r => r.Type == type).ToList();
            var gross = ofType.Where(r => r.Role == LedgerEntryRole.Principal).Sum(r => r.Amount);
            var fees = ofType.Where(r => r.Role == LedgerEntryRole.Fee).Sum(r => r.Amount);
            return type == LedgerTransactionType.Income
                ? new CashLineDto(type, label, gross + fees, Transactions(ofType), gross, fees)
                : new CashLineDto(type, label, gross + fees, Transactions(ofType));
        }

        var movements = MovementTypes.Select(m => Line(m.Type, m.Label)).ToList();
        // Earmarks move money between funds inside one account, so they always net to zero here
        var opening = Line(LedgerTransactionType.OpeningBalance, "Opening balances recorded");
        var start = before.Where(r => r.Currency == currency).Sum(r => r.Amount);
        var net = movements.Sum(m => m.Amount);
        return new CurrencyPeriodDto(currency, start, movements, net, opening, start + net + opening.Amount);
    }

    // Calendar months clipped to the period; only the most recent MaxMonths are returned
    private static (List<MonthMovementDto> Months, bool Truncated) Months(DateOnly start, DateOnly end, List<string> currencies, List<CashEntryRow> during)
    {
        var months = new List<MonthMovementDto>();
        for (var month = new DateOnly(start.Year, start.Month, 1); month <= end; month = month.AddMonths(1))
        {
            var from = month < start ? start : month;
            var to = month.AddMonths(1).AddDays(-1) is var last && last > end ? end : last;
            foreach (var currency in currencies)
            {
                var rows = during.Where(r => r.Currency == currency && r.Date >= from && r.Date <= to).ToList();
                var movement = rows.Where(r => r.Type != LedgerTransactionType.OpeningBalance).ToList();
                months.Add(new MonthMovementDto(from, to, currency,
                    rows.Where(r => r.Type == LedgerTransactionType.Income).Sum(r => r.Amount),
                    rows.Where(r => r.Type == LedgerTransactionType.Expense).Sum(r => r.Amount),
                    movement.Sum(r => r.Amount), Transactions(movement)));
            }
        }
        var monthCount = months.Select(m => m.Start).Distinct().Count();
        if (monthCount <= MaxMonths) return (months, false);
        var keep = months.Select(m => m.Start).Distinct().OrderByDescending(d => d).Take(MaxMonths).ToHashSet();
        return (months.Where(m => keep.Contains(m.Start)).ToList(), true);
    }

    // Rows are per day, type, role and currency, and a transaction has one date and one type, so its count appears
    // once per role it posted in that currency; count each transaction once by taking the larger of its roles
    private static int Transactions(IEnumerable<CashEntryRow> rows) =>
        rows.GroupBy(r => (r.Date, r.Type)).Sum(g => g.Max(r => r.Transactions));
}
