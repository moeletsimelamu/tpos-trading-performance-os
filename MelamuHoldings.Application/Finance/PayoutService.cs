using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// Trading payouts from request to money in the bank. Requesting and approving record intent only;
// receiving posts the income transaction for what was actually credited, to the Unallocated fund.
public class PayoutService
{
    private const string PayoutKeyIndex = "IX_Payouts_IdempotencyKey";

    private readonly IFinanceRepository _repository;
    private readonly LedgerPosting _posting;
    private readonly TimeProvider _clock;

    public PayoutService(IFinanceRepository repository, LedgerPosting posting, TimeProvider clock)
    {
        _repository = repository;
        _posting = posting;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<PayoutDto>> ListAsync(PayoutStatus? status)
    {
        var payouts = await _repository.GetPayoutsAsync(status);
        var allocations = await _repository.GetAllocationsAsync();
        var reversed = await ReversedAsync(allocations);
        var accounts = (await _repository.GetAccountsAsync()).ToDictionary(a => a.Id);
        return payouts.Select(p => ToDto(p, accounts, allocations.FirstOrDefault(a => a.PayoutId == p.Id && !a.IsCancelled), reversed)).ToList();
    }

    public async Task<PayoutDto> GetAsync(int id) => await ToDtoAsync(await PayoutAsync(id));

    public async Task<PayoutResult> CreateAsync(CreatePayoutRequest request, string? idempotencyKey, int userId)
    {
        var hash = Idempotency.Hash(request);
        var key = Idempotency.Key(idempotencyKey);
        if (await _repository.GetPayoutByIdempotencyKeyAsync(key) is { } previous)
            return await ReplayAsync(previous, hash);

        var currency = Currency.FromCode(request.Currency);
        var payout = new Payout(request.Source, request.Reference, request.Notes,
            Money.Of(request.Amount!.Value, currency), request.RequestedOn!.Value, new RecordingInfo(userId, Now, key, hash));

        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                _repository.Add(payout);
                await _repository.SaveChangesAsync();
                _repository.Add(Audit(userId, "payout.requested", payout, $"Recorded payout request #{payout.Id}: {payout.Requested} from {payout.Source}."));
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == PayoutKeyIndex)
        {
            var winner = await _repository.GetPayoutByIdempotencyKeyAsync(key)
                ?? throw new ConflictException("The payout could not be recorded. Please retry.");
            return await ReplayAsync(winner, hash);
        }
        return new PayoutResult(await ToDtoAsync(payout), Replayed: false);
    }

    public async Task<PayoutDto> ApproveAsync(int id, ApprovePayoutRequest request, int userId)
    {
        var payout = await PayoutAsync(id);
        var amount = request.Amount is { } a ? Money.Of(a, payout.Currency) : null;

        // Repeating an approval that already happened with the same values is a harmless retry
        if (payout.Status == PayoutStatus.Approved && payout.ApprovedOn == request.Date && payout.Approved == (amount ?? payout.Requested))
            return await ToDtoAsync(payout);

        payout.Approve(amount, request.Date!.Value, Now);
        _repository.Add(Audit(userId, "payout.approved", payout, $"Payout #{payout.Id} approved: {payout.Approved} on {payout.ApprovedOn:yyyy-MM-dd}."));
        await _repository.SaveChangesAsync();
        return await ToDtoAsync(payout);
    }

    public async Task<PayoutDto> RejectAsync(int id, RejectPayoutRequest request, int userId)
    {
        var payout = await PayoutAsync(id);
        if (payout.Status == PayoutStatus.Rejected && payout.RejectedOn == request.Date && payout.RejectionReason == request.Reason.Trim())
            return await ToDtoAsync(payout);

        payout.Reject(request.Reason, request.Date!.Value, Now);
        _repository.Add(Audit(userId, "payout.rejected", payout, $"Payout #{payout.Id} rejected: {payout.RejectionReason}"));
        await _repository.SaveChangesAsync();
        return await ToDtoAsync(payout);
    }

    public async Task<PayoutResult> ReceiveAsync(int id, ReceivePayoutRequest request, string? idempotencyKey, int userId)
    {
        Payout? payout = null;
        var (receipt, replayed) = await _posting.PostAsync(new { id, request.AccountId, request.AmountCredited, request.Fee, request.Date, request.Reference },
            idempotencyKey, userId,
            async recording =>
            {
                payout = await PayoutAsync(id);
                var account = await _repository.GetAccountAsync(request.AccountId!.Value)
                    ?? throw new DomainException($"Account {request.AccountId} does not exist.");
                var unallocated = await _repository.GetFundAsync(Fund.UnallocatedId)
                    ?? throw new DomainException("The Unallocated fund is missing.");
                var fee = request.Fee is { } f ? Money.Of(f, account.Currency) : null;
                return payout.BuildReceipt(account, unallocated, Money.Of(request.AmountCredited!.Value, account.Currency),
                    fee, request.Date!.Value, request.Reference, recording);
            },
            async receipt =>
            {
                var account = (await _repository.GetAccountAsync(request.AccountId!.Value))!;
                payout!.MarkReceived(receipt, account);
                _repository.Add(Audit(userId, "payout.received", payout,
                    $"Payout #{payout.Id} received into '{account.Name}': {payout.Credited} credited"
                    + (payout.FeeAmount is { } fee ? $" after a {account.Currency} {fee:0.00} fee" : "")
                    + $" (transaction #{receipt.Id})."));
            });

        // A replayed receipt answers with the payout it was recorded for
        var received = replayed
            ? await _repository.GetPayoutByIncomeTransactionAsync(receipt.Id)
              ?? throw new ConflictException("That receipt was undone. Record the receipt again with a new request.")
            : payout!;
        return new PayoutResult(await ToDtoAsync(received), replayed);
    }

    // Reverses the receipt's income transaction and returns the payout to Approved. Not allowed while an allocation
    // relies on the money: cancel the allocation first (which in turn requires its settlements to be reversed).
    public async Task<PayoutResult> UndoReceiptAsync(int id, UndoReceiptRequest request, string? idempotencyKey, int userId)
    {
        Payout? payout = null;
        var (reversal, replayed) = await _posting.PostAsync(new { id, undo = true, request.Date, request.Reason },
            idempotencyKey, userId,
            async recording =>
            {
                payout = await PayoutAsync(id);
                if (payout.Status != PayoutStatus.Received)
                    throw new DomainException($"Only a received payout has a receipt to undo; this one is {payout.Status.ToString().ToLowerInvariant()}.");
                if (await _repository.GetActiveAllocationForPayoutAsync(id) is { } allocation)
                    throw new DomainException($"Allocation #{allocation.Id} uses this money. Cancel it before undoing the receipt.");

                var receipt = await _repository.GetTransactionAsync(payout.IncomeTransactionId!.Value)
                    ?? throw new DomainException("The receipt transaction is missing.");
                return receipt.Reverse(new TransactionDetails(request.Date!.Value, $"Undo payout #{id} receipt: {request.Reason.Trim()}"), recording);
            },
            receipt =>
            {
                payout!.UndoReceipt();
                _repository.Add(Audit(userId, "payout.receipt_undone", payout,
                    $"Receipt of payout #{payout.Id} undone (reversal #{receipt.Id}): {request.Reason.Trim()}"));
                return Task.CompletedTask;
            });

        return new PayoutResult(await ToDtoAsync(payout ?? await PayoutAsync(id)), replayed);
    }

    private async Task<PayoutResult> ReplayAsync(Payout previous, string hash) =>
        previous.RequestHash == hash
            ? new PayoutResult(await ToDtoAsync(previous), Replayed: true)
            : throw new ConflictException(Idempotency.KeyReusedMessage);

    private async Task<Payout> PayoutAsync(int id) =>
        await _repository.GetPayoutAsync(id) ?? throw new NotFoundException($"Payout {id} does not exist.");

    private async Task<PayoutDto> ToDtoAsync(Payout payout)
    {
        var allocation = await _repository.GetActiveAllocationForPayoutAsync(payout.Id);
        var reversed = allocation is null ? new HashSet<int>() : await ReversedAsync([allocation]);
        var accounts = (await _repository.GetAccountsAsync()).ToDictionary(a => a.Id);
        return ToDto(payout, accounts, allocation, reversed);
    }

    private async Task<HashSet<int>> ReversedAsync(IEnumerable<Allocation> allocations)
    {
        var ids = allocations.SelectMany(a => a.Lines).SelectMany(l => l.Settlements).Select(s => s.LedgerTransactionId).ToList();
        return ids.Count == 0 ? [] : (await _repository.GetReversalIdsAsync(ids)).Keys.ToHashSet();
    }

    private static PayoutDto ToDto(Payout p, IReadOnlyDictionary<int, FinancialAccount> accounts, Allocation? allocation, IReadOnlySet<int> reversed) => new(
        p.Id, p.Source, p.Reference, p.Notes, p.Status,
        MoneyDto.From(p.Requested), p.RequestedOn,
        p.Approved is { } approved ? MoneyDto.From(approved) : null, p.ApprovedOn,
        p.ReceivedOn, p.ReceivedIntoAccountId,
        p.ReceivedIntoAccountId is { } accountId && accounts.TryGetValue(accountId, out var account) ? account.Name : null,
        p.Credited is { } credited ? MoneyDto.From(credited) : null,
        p.FeeAmount is { } fee && p.CreditedCurrency is not null ? new MoneyDto(fee, p.CreditedCurrency.Code) : null,
        p.EffectiveRate, p.IncomeTransactionId,
        p.RejectedOn, p.RejectionReason, p.CreatedAt,
        allocation?.Id, allocation?.Status(reversed));

    private AuditEvent Audit(int userId, string action, Payout payout, string summary) =>
        AuditLog.Event(Now, userId, action, nameof(Payout), payout.Id, summary, new
        {
            payout.Source, Status = payout.Status.ToString(), Currency = payout.Currency.Code,
            payout.RequestedAmount, payout.ApprovedAmount, payout.ReceivedOn, payout.ReceivedIntoAccountId,
            payout.CreditedAmount, payout.FeeAmount, payout.EffectiveRate, payout.IncomeTransactionId, payout.RejectionReason
        });
}
