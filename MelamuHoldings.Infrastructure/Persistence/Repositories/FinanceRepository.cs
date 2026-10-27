using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Finance;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Infrastructure.Persistence.Repositories;

public partial class FinanceRepository : IFinanceRepository
{
    private readonly AppDbContext _context;

    public FinanceRepository(AppDbContext context)
    {
        _context = context;
    }

    public Task<List<Fund>> GetFundsAsync() =>
        _context.Funds.OrderBy(f => f.Id).ToListAsync();

    public Task<Fund?> GetFundAsync(int id) =>
        _context.Funds.FirstOrDefaultAsync(f => f.Id == id);

    public Task<bool> FundNameExistsAsync(string name, int? excludingId = null) =>
        _context.Funds.AnyAsync(f => f.Name == name.Trim() && f.Id != excludingId);

    public Task<List<FinancialAccount>> GetAccountsAsync() =>
        _context.FinancialAccounts.OrderBy(a => a.Name).ToListAsync();

    public Task<FinancialAccount?> GetAccountAsync(int id) =>
        _context.FinancialAccounts.FirstOrDefaultAsync(a => a.Id == id);

    public Task<bool> AccountNameExistsAsync(string name, int? excludingId = null) =>
        _context.FinancialAccounts.AnyAsync(a => a.Name == name.Trim() && a.Id != excludingId);

    public Task<LedgerTransaction?> GetTransactionAsync(int id) =>
        _context.LedgerTransactions.Include(t => t.Entries).FirstOrDefaultAsync(t => t.Id == id);

    public Task<LedgerTransaction?> GetTransactionByIdempotencyKeyAsync(string idempotencyKey) =>
        _context.LedgerTransactions.AsNoTracking().Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey);

    public async Task<PagedResult<LedgerTransaction>> GetTransactionsAsync(TransactionQuery query)
    {
        var transactions = _context.LedgerTransactions.AsNoTracking();
        if (query.From is { } from) transactions = transactions.Where(t => t.TransactionDate >= from);
        if (query.To is { } to) transactions = transactions.Where(t => t.TransactionDate <= to);
        if (query.Type is { } type) transactions = transactions.Where(t => t.Type == type);
        if (query.AccountId is { } accountId) transactions = transactions.Where(t => t.Entries.Any(e => e.FinancialAccountId == accountId));
        if (query.FundId is { } fundId) transactions = transactions.Where(t => t.Entries.Any(e => e.FundId == fundId));

        var total = await transactions.CountAsync();
        var items = await transactions
            .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Include(t => t.Entries)
            .ToListAsync();

        return new PagedResult<LedgerTransaction>(items, query.Page, query.PageSize, total);
    }

    public Task<bool> HasOpeningBalanceAsync(int accountId, int fundId) =>
        _context.LedgerTransactions.AnyAsync(t =>
            t.Type == LedgerTransactionType.OpeningBalance
            && t.Entries.Any(e => e.FinancialAccountId == accountId && e.FundId == fundId)
            && !_context.LedgerTransactions.Any(r => r.ReversesTransactionId == t.Id));

    public async Task<Dictionary<int, int>> GetReversalIdsAsync(IEnumerable<int> transactionIds)
    {
        var ids = transactionIds.ToList();
        return await _context.LedgerTransactions
            .Where(r => r.ReversesTransactionId != null && ids.Contains(r.ReversesTransactionId.Value))
            .ToDictionaryAsync(r => r.ReversesTransactionId!.Value, r => r.Id);
    }

    public async Task<List<PositionRow>> GetPositionsAsync(DateOnly? asOf = null)
    {
        var entries = _context.LedgerEntries.AsQueryable();
        if (asOf is { } date)
            entries = entries.Where(e => _context.LedgerTransactions.Any(t => t.Id == e.LedgerTransactionId && t.TransactionDate <= date));

        var rows = await entries
            .GroupBy(e => new { e.FinancialAccountId, e.FundId, e.Currency })
            .Select(g => new { g.Key.FinancialAccountId, g.Key.FundId, g.Key.Currency, Amount = g.Sum(e => e.Amount) })
            .ToListAsync();

        return rows.Select(r => new PositionRow(r.FinancialAccountId, r.FundId, r.Currency, r.Amount)).ToList();
    }

    public async Task<decimal> GetAccountBalanceAsync(int accountId) =>
        await _context.LedgerEntries.Where(e => e.FinancialAccountId == accountId).SumAsync(e => (decimal?)e.Amount) ?? 0m;

    public async Task<PagedResult<AuditEvent>> GetAuditEventsAsync(int page, int pageSize)
    {
        var total = await _context.AuditEvents.CountAsync();
        var items = await _context.AuditEvents.AsNoTracking()
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return new PagedResult<AuditEvent>(items, page, pageSize, total);
    }

    public void Add(Fund fund) => _context.Funds.Add(fund);
    public void Add(FinancialAccount account) => _context.FinancialAccounts.Add(account);
    public void Add(LedgerTransaction transaction) => _context.LedgerTransactions.Add(transaction);
    public void Add(AuditEvent auditEvent) => _context.AuditEvents.Add(auditEvent);

    public async Task SaveChangesAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            // Leave the context clean so the caller can read the row that won
            _context.ChangeTracker.Clear();
            var name = IndexNamePattern().Match(sql.Message) is { Success: true } m ? m.Groups[1].Value : "unknown";
            throw new UniqueConstraintViolationException(name, ex);
        }
    }

    [GeneratedRegex(@"(?:index|constraint) '([^']+)'")]
    private static partial Regex IndexNamePattern();
}
