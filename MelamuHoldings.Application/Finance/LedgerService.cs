using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// Records money movements. Each use case builds a transaction through the domain factory for its type,
// so there is no way to set a balance directly: balances only ever change through posted entries.
public class LedgerService
{
    public const int MaxPageSize = 100;
    private readonly IFinanceRepository _repository;
    private readonly LedgerPosting _posting;

    public LedgerService(IFinanceRepository repository, LedgerPosting posting)
    {
        _repository = repository;
        _posting = posting;
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
                Details(request), recording, Fee(request, from, to), request.QuotedRate);
        });

    public Task<RecordResult> ReverseAsync(int transactionId, ReverseRequest request, string? idempotencyKey, int userId) =>
        RecordAsync(new { transactionId, request.Date, request.Reason }, idempotencyKey, userId, async recording =>
        {
            var original = await _repository.GetTransactionAsync(transactionId)
                ?? throw new NotFoundException($"Transaction {transactionId} does not exist.");
            // A payout's receipt is undone from the payout, which also returns it to Approved
            if (await _repository.GetPayoutByIncomeTransactionAsync(transactionId) is { } payout)
                throw new DomainException($"Transaction {transactionId} records the receipt of payout #{payout.Id}. Undo the receipt from the payout instead.");
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

    private async Task<RecordResult> RecordAsync(object request, string? idempotencyKey, int userId, Func<RecordingInfo, Task<LedgerTransaction>> build)
    {
        var (transaction, replayed) = await _posting.PostAsync(request, idempotencyKey, userId, build);
        return new RecordResult(await ToDtoAsync(transaction), replayed);
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

    private static Money? Fee(ExchangeRequest request, FinancialAccount from, FinancialAccount to) =>
        request.Fee is not { } fee ? null
        : Money.Of(fee, request.FeeChargedTo == FeeChargedTo.Receiving ? to.Currency : from.Currency);

    private static TransactionDetails Details(TransactionRequest request, string? category = null) =>
        new(request.Date!.Value, request.Description, request.Reference, category);
}
