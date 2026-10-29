using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// Turns accounts, funds and summed ledger positions into response DTOs.
// Every balance here is derived from ledger entries; none is stored or edited directly.
public class FinanceReadModel
{
    private readonly Dictionary<int, FinancialAccount> _accounts;
    private readonly Dictionary<int, Fund> _funds;
    private readonly List<PositionRow> _positions;

    public FinanceReadModel(IEnumerable<FinancialAccount> accounts, IEnumerable<Fund> funds, IEnumerable<PositionRow> positions)
    {
        _accounts = accounts.ToDictionary(a => a.Id);
        _funds = funds.ToDictionary(f => f.Id);
        _positions = positions.ToList();
    }

    public static async Task<FinanceReadModel> LoadAsync(IFinanceRepository repository, DateOnly? asOf = null) =>
        new(await repository.GetAccountsAsync(), await repository.GetFundsAsync(), await repository.GetPositionsAsync(asOf));

    public AccountDto Account(FinancialAccount account)
    {
        var rows = _positions.Where(p => p.FinancialAccountId == account.Id).ToList();
        var balance = Money.Of(rows.Sum(p => p.Amount), account.Currency);
        var funds = rows
            .Where(p => p.Amount != 0)
            .OrderBy(p => p.FundId)
            .Select(p => new FundPositionDto(p.FundId, FundName(p.FundId), new MoneyDto(p.Amount, p.Currency.Code)))
            .ToList();

        return new AccountDto(
            account.Id, account.Name, account.Type, account.Currency.Code, account.Ownership,
            account.Institution, account.Notes, account.Status,
            MoneyDto.From(balance), funds, account.CreatedAt, account.ClosedAt);
    }

    public IReadOnlyList<AccountDto> Accounts() => _accounts.Values.OrderBy(a => a.Name).Select(Account).ToList();

    public FundDto Fund(Fund fund)
    {
        var balances = _positions
            .Where(p => p.FundId == fund.Id)
            .GroupBy(p => p.Currency.Code)
            .Select(g => new MoneyDto(g.Sum(p => p.Amount), g.Key))
            .Where(m => m.Amount != 0)
            .OrderBy(m => m.Currency)
            .ToList();
        return new FundDto(fund.Id, fund.Name, fund.Description, fund.IsSystem, balances);
    }

    public IReadOnlyList<FundDto> Funds() => _funds.Values.OrderBy(f => f.Id).Select(Fund).ToList();

    public IReadOnlyList<MoneyDto> CashTotals() => _positions
        .Where(p => _accounts.TryGetValue(p.FinancialAccountId, out var a) && a.Type is FinancialAccountType.Bank or FinancialAccountType.Cash)
        .GroupBy(p => p.Currency.Code)
        .Select(g => new MoneyDto(g.Sum(p => p.Amount), g.Key))
        .OrderBy(m => m.Currency)
        .ToList();

    public TransactionDto Transaction(LedgerTransaction transaction, int? reversedBy) => new(
        transaction.Id, transaction.Type, transaction.TransactionDate, transaction.RecordedAt,
        transaction.Description, transaction.Reference, transaction.Category, transaction.ExchangeRate, transaction.QuotedRate,
        transaction.ReversesTransactionId, reversedBy,
        transaction.Entries
            .OrderBy(e => e.Role).ThenBy(e => e.Amount)
            .Select(e => new EntryDto(e.FinancialAccountId, AccountName(e.FinancialAccountId), e.FundId, FundName(e.FundId), e.Amount, e.Currency.Code, e.Role))
            .ToList());

    // The (account, fund) balance in the account's currency
    public Money Position(FinancialAccount account, int fundId) => Money.Of(
        _positions.Where(p => p.FinancialAccountId == account.Id && p.FundId == fundId).Sum(p => p.Amount), account.Currency);

    public string AccountName(int id) => _accounts.TryGetValue(id, out var a) ? a.Name : $"Account {id}";
    public string FundName(int id) => _funds.TryGetValue(id, out var f) ? f.Name : $"Fund {id}";
}
