using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// One (account, fund, currency) bucket of money, summed from ledger entries
public record PositionRow(int FinancialAccountId, int FundId, Currency Currency, decimal Amount);

public record TransactionQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    LedgerTransactionType? Type = null,
    int? AccountId = null,
    int? FundId = null,
    int Page = 1,
    int PageSize = 25);

public interface IFinanceRepository
{
    Task<List<Fund>> GetFundsAsync();
    Task<Fund?> GetFundAsync(int id);
    Task<bool> FundNameExistsAsync(string name, int? excludingId = null);

    Task<List<FinancialAccount>> GetAccountsAsync();
    Task<FinancialAccount?> GetAccountAsync(int id);
    Task<bool> AccountNameExistsAsync(string name, int? excludingId = null);

    Task<LedgerTransaction?> GetTransactionAsync(int id);
    Task<LedgerTransaction?> GetTransactionByIdempotencyKeyAsync(string idempotencyKey);
    Task<PagedResult<LedgerTransaction>> GetTransactionsAsync(TransactionQuery query);

    // Opening balances that have been reversed do not count
    Task<bool> HasOpeningBalanceAsync(int accountId, int fundId);

    // Maps each given transaction id to the id of the transaction that reversed it, if any
    Task<Dictionary<int, int>> GetReversalIdsAsync(IEnumerable<int> transactionIds);

    Task<List<PositionRow>> GetPositionsAsync(DateOnly? asOf = null);
    Task<decimal> GetAccountBalanceAsync(int accountId);

    Task<PagedResult<AuditEvent>> GetAuditEventsAsync(int page, int pageSize);

    void Add(Fund fund);
    void Add(FinancialAccount account);
    void Add(LedgerTransaction transaction);
    void Add(AuditEvent auditEvent);

    // Saves everything added or changed. Throws UniqueConstraintViolationException when a unique index rejects the write.
    Task SaveChangesAsync();

    // Runs several saves atomically, e.g. a transaction and the audit event that needs its generated id
    Task<T> InTransactionAsync<T>(Func<Task<T>> work);
}
