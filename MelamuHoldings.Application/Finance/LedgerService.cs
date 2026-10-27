using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// Records money movements. Each use case builds a transaction through the domain factory for its type,
// so there is no way to set a balance directly: balances only ever change through posted entries.
public class LedgerService
{
    public const int MaxPageSize = 100;
    private const string IdempotencyIndex = "IX_LedgerTransactions_IdempotencyKey";
    private const string ReversalIndex = "IX_LedgerTransactions_ReversesTransactionId";

    private readonly IFinanceRepository _repository;
    private readonly TimeProvider _clock;

    public LedgerService(IFinanceRepository repository, TimeProvider clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public Task<RecordResult> RecordOpeningBalanceAsync(OpeningBalanceRequest request, string? idempotencyKey, int userId) =>
        RecordAsync(request, idempotencyKey, userId, async recording =>
        {
            var account = await AccountAsync(request.AccountId!.Value);
            var fund = await FundAsync(request.FundId!.Value);
            if (await _repository.HasOpeningBalanceAsync(account.Id, fund.Id))
                throw new DomainException($"'{account.Name}' already has an opening balance for '{fund.Name}'. Reverse it first to replace it.");
            return LedgerTransaction.OpeningBalance(account, fund, Money.Of(request.Amount!.Value, account.Currency), Details(request), recording);
        });

    public Task<RecordResult> RecordIncomeAsync(IncomeRequest request, string? idempotencyKey, int userId) =>
        RecordAsync(request, idempotencyKey, userId, async recording =>
        {
            var account = await AccountAsync(request.AccountId!.Value);
            var fund = await FundAsync(request.FundId!.Value);
            return LedgerTransaction.Income(account, fund, Money.Of(request.Amount!.Value, account.Currency), Details(request, request.Category), recording);
        });

    public Task<RecordResult> RecordExpenseAsync(ExpenseRequest request, string? idempotencyKey, int userId) =>
        RecordAsync(request, idempotencyKey, userId, async recording =>
        {
            var account = await AccountAsync(request.AccountId!.Value);
            var fund = await FundAsync(request.FundId!.Value);
            return LedgerTransaction.Expense(account, fund, Money.Of(request.Amount!.Value, account.Currency), Details(request, request.Category), recording);
        });

    public Task<RecordResult> RecordTransferAsync(TransferRequest request, string? idempotencyKey, int userId) =>
        RecordAsync(request, idempotencyKey, userId, async recording =>
        {
            var from = await AccountAsync(request.FromAccountId!.Value);
            var to = await AccountAsync(request.ToAccountId!.Value);
            return LedgerTransaction.Transfer(
                from, await FundAsync(request.FromFundId!.Value), to, await FundAsync(request.ToFundId!.Value),
                Money.Of(request.Amount!.Value, from.Currency), Details(request), recording);
        });

    public Task<RecordResult> RecordReallocationAsync(ReallocationRequest request, string? idempotencyKey, int userId) =>
        RecordAsync(request, idempotencyKey, userId, async recording =>
        {
            var account = await AccountAsync(request.AccountId!.Value);
            return LedgerTransaction.FundReallocation(
                account, await FundAsync(request.FromFundId!.Value), await FundAsync(request.ToFundId!.Value),
                Money.Of(request.Amount!.Value, account.Currency), Details(request), recording);
        });

    public Task<RecordResult> RecordExchangeAsync(ExchangeRequest request, string? idempotencyKey, int userId) =>
        RecordAsync(request, idempotencyKey, userId, async recording =>
        {
            var from = await AccountAsync(request.FromAccountId!.Value);
            var to = await AccountAsync(request.ToAccountId!.Value);
            return LedgerTransaction.CurrencyExchange(
                from, await FundAsync(request.FromFundId!.Value), Money.Of(request.AmountSent!.Value, from.Currency),
                to, await FundAsync(request.ToFundId!.Value), Money.Of(request.AmountReceived!.Value, to.Currency),
                Details(request), recording);
        });

    public Task<RecordResult> ReverseAsync(int transactionId, ReverseRequest request, string? idempotencyKey, int userId) =>
        RecordAsync(new { transactionId, request.Date, request.Reason }, idempotencyKey, userId, async recording =>
        {
            var original = await _repository.GetTransactionAsync(transactionId)
                ?? throw new NotFoundException($"Transaction {transactionId} does not exist.");
            if ((await _repository.GetReversalIdsAsync([transactionId])).Count > 0)
                throw new ConflictException($"Transaction {transactionId} has already been reversed.");

            // A closed account must stay at zero, so nothing may post to it, including a reversal
            foreach (var accountId in original.Entries.Select(e => e.FinancialAccountId).Distinct())
                (await AccountAsync(accountId)).EnsureActive();

            return original.Reverse(new TransactionDetails(request.Date!.Value, request.Reason), recording);
        });

    public async Task<TransactionDto> GetAsync(int id)
    {
        var transaction = await _repository.GetTransactionAsync(id) ?? throw new NotFoundException($"Transaction {id} does not exist.");
        return await ToDtoAsync(transaction);
    }

    public async Task<PagedResult<TransactionDto>> ListAsync(TransactionQuery query)
    {
        var page = await _repository.GetTransactionsAsync(query);
        var reversals = await _repository.GetReversalIdsAsync(page.Items.Select(t => t.Id));
        var readModel = await FinanceReadModel.LoadAsync(_repository);
        return new PagedResult<TransactionDto>(
            page.Items.Select(t => readModel.Transaction(t, reversals.TryGetValue(t.Id, out var r) ? r : null)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<BalancesDto> GetBalancesAsync(DateOnly? asOf)
    {
        var readModel = await FinanceReadModel.LoadAsync(_repository, asOf);
        return new BalancesDto(asOf, readModel.Accounts(), readModel.Funds(), readModel.CashTotals());
    }

    // The single path every money movement takes: idempotency check, build, then save with its audit event
    private async Task<RecordResult> RecordAsync(object request, string? idempotencyKey, int userId, Func<RecordingInfo, Task<LedgerTransaction>> build)
    {
        var requestHash = Hash(request);
        // Without a client key the request still gets a unique one, so it posts exactly once
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey.Trim();

        if (await _repository.GetTransactionByIdempotencyKeyAsync(key) is { } previous)
            return await ReplayAsync(previous, requestHash);

        var now = _clock.GetUtcNow().UtcDateTime;
        var transaction = await build(new RecordingInfo(userId, now, key, requestHash));

        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                _repository.Add(transaction);
                await _repository.SaveChangesAsync();
                _repository.Add(AuditLog.Event(now, userId, "transaction.recorded", nameof(LedgerTransaction), transaction.Id,
                    Summarise(transaction), new
                    {
                        Type = transaction.Type.ToString(), Date = transaction.TransactionDate, transaction.Description,
                        transaction.ReversesTransactionId, transaction.ExchangeRate,
                        Entries = transaction.Entries.Select(e => new { e.FinancialAccountId, e.FundId, e.Amount, Currency = e.Currency.Code })
                    }));
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == IdempotencyIndex)
        {
            // A concurrent retry with the same key won the race: answer with its transaction
            var winner = await _repository.GetTransactionByIdempotencyKeyAsync(key)
                ?? throw new ConflictException("The request could not be recorded. Please retry.");
            return await ReplayAsync(winner, requestHash);
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == ReversalIndex)
        {
            throw new ConflictException($"Transaction {transaction.ReversesTransactionId} has already been reversed.");
        }

        return new RecordResult(await ToDtoAsync(transaction), Replayed: false);
    }

    private async Task<RecordResult> ReplayAsync(LedgerTransaction previous, string requestHash)
    {
        if (previous.RequestHash != requestHash)
            throw new ConflictException("This Idempotency-Key was already used for a different request.");
        return new RecordResult(await ToDtoAsync(previous), Replayed: true);
    }

    private async Task<TransactionDto> ToDtoAsync(LedgerTransaction transaction)
    {
        var reversals = await _repository.GetReversalIdsAsync([transaction.Id]);
        var readModel = await FinanceReadModel.LoadAsync(_repository);
        return readModel.Transaction(transaction, reversals.TryGetValue(transaction.Id, out var r) ? r : null);
    }

    private async Task<FinancialAccount> AccountAsync(int id) =>
        await _repository.GetAccountAsync(id) ?? throw new DomainException($"Account {id} does not exist.");

    private async Task<Fund> FundAsync(int id) =>
        await _repository.GetFundAsync(id) ?? throw new DomainException($"Fund {id} does not exist.");

    private static TransactionDetails Details(TransactionRequest request, string? category = null) =>
        new(request.Date!.Value, request.Description, request.Reference, category);

    private static string Summarise(LedgerTransaction t) =>
        $"Recorded {t.Type} dated {t.TransactionDate:yyyy-MM-dd} ({string.Join(", ", t.Entries.Select(e => e.Money))}): {t.Description}";

    // Identifies the request body so a reused Idempotency-Key with a different payload is rejected
    private static string Hash(object request)
    {
        var payload = request.GetType().FullName + JsonSerializer.Serialize(request, request.GetType());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}
