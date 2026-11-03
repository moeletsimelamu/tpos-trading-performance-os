using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

public class CreateBudgetLineRequest
{
    [Required] public string Month { get; set; } = string.Empty;   // yyyy-MM
    [Required, MaxLength(LedgerTransaction.MaxCategoryLength)] public string Category { get; set; } = string.Empty;
    [Required] public decimal? Amount { get; set; }
    [Required] public string Currency { get; set; } = string.Empty;
}

public class UpdateBudgetLineRequest
{
    [Required, MaxLength(LedgerTransaction.MaxCategoryLength)] public string Category { get; set; } = string.Empty;
    [Required] public decimal? Amount { get; set; }
}

// Remaining is below zero when the budget is exceeded. UsedPercent is not capped, so 130 means 30% over.
public record BudgetLineDto(
    int Id, string Month, string Category, MoneyDto Budget, MoneyDto Actual, MoneyDto Remaining, decimal UsedPercent, bool Exceeded, int Transactions);

// Expenses in the month with no budget line for that category and currency. Category is null when none was recorded.
public record UnbudgetedSpendDto(string? Category, MoneyDto Actual, int Transactions);

public record BudgetTotalDto(string Currency, decimal Budget, decimal ActualBudgeted, decimal ActualUnbudgeted);

public record BudgetMonthDto(string Month, DateOnly From, DateOnly To, IReadOnlyList<BudgetLineDto> Lines, IReadOnlyList<UnbudgetedSpendDto> Unbudgeted, IReadOnlyList<BudgetTotalDto> Totals);

// Budget versus actual per month and category. Actual is the month's expense transactions with that category, in that
// currency, by transaction date; a reversed expense counts for nothing. Budgets are plans: they never post to the ledger.
public class BudgetService
{
    public const string LineIndex = "IX_BudgetLines_Month_Category_Currency";
    private const string MonthFormat = "yyyy-MM";

    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public BudgetService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // This month unless one is given
    public async Task<BudgetMonthDto> GetMonthAsync(string? month)
    {
        var today = DateOnly.FromDateTime(Now);
        var start = string.IsNullOrWhiteSpace(month) ? new DateOnly(today.Year, today.Month, 1) : ParseMonth(month);
        var end = start.AddMonths(1).AddDays(-1);

        var lines = (await _repository.GetBudgetLinesAsync(start)).OrderBy(l => l.Category).ThenBy(l => l.Currency.Code).ToList();
        var expenses = await _repository.GetUnreversedExpensesAsync(start, end);
        var budgeted = lines.Select(l => (BudgetLine.CategoryKey(l.Category), l.Currency.Code)).ToHashSet();

        var lineDtos = lines.Select(l => Line(l, expenses)).ToList();
        var unbudgeted = expenses
            .Where(e => !budgeted.Contains((BudgetLine.CategoryKey(e.Category), e.Currency)))
            .GroupBy(e => (Key: BudgetLine.CategoryKey(e.Category), e.Currency))
            .Select(g => new UnbudgetedSpendDto(g.Select(e => e.Category?.Trim()).First(), new MoneyDto(g.Sum(e => e.Amount), g.Key.Currency), g.Select(e => e.TransactionId).Distinct().Count()))
            .OrderByDescending(u => u.Category is not null).ThenBy(u => u.Category).ThenBy(u => u.Actual.Currency)
            .ToList();

        var currencies = lineDtos.Select(l => l.Budget.Currency).Concat(unbudgeted.Select(u => u.Actual.Currency)).Distinct().OrderBy(c => c);
        var totals = currencies.Select(c => new BudgetTotalDto(c,
            lineDtos.Where(l => l.Budget.Currency == c).Sum(l => l.Budget.Amount),
            lineDtos.Where(l => l.Budget.Currency == c).Sum(l => l.Actual.Amount),
            unbudgeted.Where(u => u.Actual.Currency == c).Sum(u => u.Actual.Amount))).ToList();

        return new BudgetMonthDto(start.ToString(MonthFormat, CultureInfo.InvariantCulture), start, end, lineDtos, unbudgeted, totals);
    }

    public async Task<BudgetLineDto> CreateAsync(CreateBudgetLineRequest request, int userId)
    {
        var line = new BudgetLine(ParseMonth(request.Month), request.Category, Money.Of(request.Amount!.Value, Currency.FromCode(request.Currency)), Now);
        await SaveWithAuditAsync(() => _repository.Add(line), () => Audit(userId, "budget.created", line, $"Budgeted {Describe(line)}.", Snapshot(line)));
        return await DtoAsync(line);
    }

    public async Task<BudgetLineDto> UpdateAsync(int id, UpdateBudgetLineRequest request, int userId)
    {
        var line = await LineAsync(id);
        var before = Snapshot(line);
        line.Update(request.Category, Money.Of(request.Amount!.Value, line.Currency), Now);
        await SaveWithAuditAsync(() => { }, () => Audit(userId, "budget.updated", line, $"Changed the budget to {Describe(line)}.", new { before, after = Snapshot(line) }));
        return await DtoAsync(line);
    }

    // A budget is a plan, not a record of money, so a line can be removed (and the removal audited)
    public async Task DeleteAsync(int id, int userId)
    {
        var line = await LineAsync(id);
        await SaveWithAuditAsync(
            () => { _repository.Remove(line); _repository.Add(Audit(userId, "budget.deleted", line, $"Removed the budget of {Describe(line)}.", Snapshot(line))); },
            () => null);
    }

    private async Task<BudgetLineDto> DtoAsync(BudgetLine line) =>
        Line(line, await _repository.GetUnreversedExpensesAsync(line.Month, line.Month.AddMonths(1).AddDays(-1)));

    private static BudgetLineDto Line(BudgetLine line, List<ExpenseRow> expenses)
    {
        var key = BudgetLine.CategoryKey(line.Category);
        var mine = expenses.Where(e => e.Currency == line.Currency.Code && BudgetLine.CategoryKey(e.Category) == key).ToList();
        var actual = mine.Sum(e => e.Amount);
        var currency = line.Currency.Code;
        return new BudgetLineDto(
            line.Id, line.Month.ToString(MonthFormat, CultureInfo.InvariantCulture), line.Category, new MoneyDto(line.Amount, currency), new MoneyDto(actual, currency),
            new MoneyDto(line.Amount - actual, currency), Math.Round(actual / line.Amount * 100, 1, MidpointRounding.ToZero), actual > line.Amount,
            mine.Select(e => e.TransactionId).Distinct().Count());
    }

    private static DateOnly ParseMonth(string month) =>
        DateOnly.TryParseExact(month.Trim(), MonthFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new DomainException("Give the month as yyyy-MM.");

    private async Task<BudgetLine> LineAsync(int id) =>
        await _repository.GetBudgetLineAsync(id) ?? throw new NotFoundException($"Budget line {id} does not exist.");

    private static string Describe(BudgetLine l) => $"{l.Category} in {l.Month.ToString(MonthFormat, CultureInfo.InvariantCulture)}: {l.Currency.Code} {l.Amount:0.00}";

    private static object Snapshot(BudgetLine l) => new { Month = l.Month.ToString(MonthFormat, CultureInfo.InvariantCulture), l.Category, l.Amount, Currency = l.Currency.Code };

    private AuditEvent Audit(int userId, string action, BudgetLine l, string summary, object data) =>
        AuditLog.Event(Now, userId, action, nameof(BudgetLine), l.Id, summary, data);

    // Saves the change, then the audit event that references its id, atomically
    private async Task SaveWithAuditAsync(Action change, Func<AuditEvent?> audit)
    {
        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                change();
                await _repository.SaveChangesAsync();
                if (audit() is { } auditEvent)
                {
                    _repository.Add(auditEvent);
                    await _repository.SaveChangesAsync();
                }
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == LineIndex)
        {
            throw new ConflictException("That month already has a budget for this category and currency.");
        }
    }
}
