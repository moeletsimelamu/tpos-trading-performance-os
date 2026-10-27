using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Finance;

namespace MelamuHoldings.Application.Finance;

// Allocation policies and the allocation of received payouts. Preview is pure calculation; confirming stores the
// split as a commitment; settling posts real ledger transactions against it. Status is always derived from the
// settlements that still stand, so reversing a settlement transaction reopens its line automatically.
public class AllocationService
{
    private const string AllocationKeyIndex = "IX_Allocations_IdempotencyKey";
    private const string OneActiveAllocationIndex = "IX_Allocations_PayoutId";
    private const string PolicyVersionIndex = "IX_AllocationPolicies_Version";

    private readonly IFinanceRepository _repository;
    private readonly LedgerPosting _posting;
    private readonly TimeProvider _clock;

    public AllocationService(IFinanceRepository repository, LedgerPosting posting, TimeProvider clock)
    {
        _repository = repository;
        _posting = posting;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // ---- Policies ----

    public async Task<IReadOnlyList<AllocationPolicyDto>> ListPoliciesAsync()
    {
        var funds = await FundsAsync();
        return (await _repository.GetPoliciesAsync()).OrderByDescending(p => p.Version).Select(p => ToDto(p, funds)).ToList();
    }

    public async Task<AllocationPolicyDto> CreatePolicyAsync(CreatePolicyRequest request, int userId)
    {
        var funds = await FundsAsync();
        var lines = request.Lines.Select(l => (
            funds.TryGetValue(l.FundId!.Value, out var fund) ? fund : throw new DomainException($"Fund {l.FundId} does not exist."),
            l.Percentage!.Value)).ToList();

        var policy = new AllocationPolicy(await _repository.GetLatestPolicyVersionAsync() + 1, lines, Now, userId);
        var previous = request.Activate ? await _repository.GetActivePolicyAsync() : null;

        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                if (request.Activate)
                {
                    // Deactivate first: the database allows only one active policy at a time
                    previous?.Deactivate();
                    await _repository.SaveChangesAsync();
                    policy.Activate(Now);
                }
                _repository.Add(policy);
                await _repository.SaveChangesAsync();
                _repository.Add(AuditLog.Event(Now, userId, "policy.created", nameof(AllocationPolicy), policy.Id,
                    $"Created allocation policy v{policy.Version} ({Describe(policy, funds)}){(request.Activate ? ", now active" : "")}.",
                    new { policy.Version, policy.IsActive, Lines = policy.Lines.Select(l => new { l.FundId, l.Percentage }) }));
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == PolicyVersionIndex)
        {
            throw new ConflictException("Another policy version was created at the same time. Reload and try again.");
        }
        return ToDto(policy, funds);
    }

    public async Task<AllocationPolicyDto> ActivatePolicyAsync(int id, int userId)
    {
        var policy = await _repository.GetPolicyAsync(id) ?? throw new NotFoundException($"Allocation policy {id} does not exist.");
        var funds = await FundsAsync();
        if (policy.IsActive) return ToDto(policy, funds);

        var previous = await _repository.GetActivePolicyAsync();
        await _repository.InTransactionAsync(async () =>
        {
            previous?.Deactivate();
            await _repository.SaveChangesAsync();
            policy.Activate(Now);
            _repository.Add(AuditLog.Event(Now, userId, "policy.activated", nameof(AllocationPolicy), policy.Id,
                $"Activated allocation policy v{policy.Version} ({Describe(policy, funds)})" + (previous is null ? "." : $", replacing v{previous.Version}."),
                new { policy.Version, PreviousVersion = previous?.Version }));
            await _repository.SaveChangesAsync();
            return true;
        });
        return ToDto(policy, funds);
    }

    // ---- Allocations ----

    public async Task<AllocationPreviewDto> PreviewAsync(AllocationPreviewRequest request)
    {
        var payout = await PayoutAsync(request.PayoutId!.Value);
        await EnsureNotAllocatedAsync(payout);
        var policy = await PolicyAsync(request.PolicyId);
        if (payout.Status != PayoutStatus.Received || payout.Credited is not { } credited)
            throw new DomainException("Only a received payout can be allocated: until the money has arrived there is nothing to split.");

        var funds = await FundsAsync();
        var account = (await _repository.GetAccountAsync(payout.ReceivedIntoAccountId!.Value))!;
        var lines = policy.Lines.OrderBy(l => l.Position).ToList();
        var shares = policy.Split(credited);
        return new AllocationPreviewDto(payout.Id, policy.Id, policy.Version, account.Id, account.Name, MoneyDto.From(credited),
            lines.Select((l, i) => new PreviewLineDto(l.FundId, FundName(funds, l.FundId), l.Percentage, MoneyDto.From(shares[i]))).ToList());
    }

    public async Task<AllocationResult> ConfirmAsync(ConfirmAllocationRequest request, string? idempotencyKey, int userId)
    {
        var hash = Idempotency.Hash(request);
        var key = Idempotency.Key(idempotencyKey);
        if (await _repository.GetAllocationByIdempotencyKeyAsync(key) is { } previous)
            return await ReplayAsync(previous, hash);

        var payout = await PayoutAsync(request.PayoutId!.Value);
        Allocation allocation;
        try
        {
            await EnsureNotAllocatedAsync(payout);
            var policy = await _repository.GetPolicyAsync(request.PolicyId!.Value)
                ?? throw new DomainException($"Allocation policy {request.PolicyId} does not exist.");
            allocation = Allocation.Confirm(payout, policy, await FundsAsync(), new RecordingInfo(userId, Now, key, hash));
        }
        catch (ConflictException)
        {
            // "Already allocated" may be this very request's earlier attempt, committed since the key check above
            if (await _repository.GetAllocationByIdempotencyKeyAsync(key) is { } winner)
                return await ReplayAsync(winner, hash);
            throw;
        }

        try
        {
            await _repository.InTransactionAsync(async () =>
            {
                _repository.Add(allocation);
                await _repository.SaveChangesAsync();
                _repository.Add(AuditLog.Event(Now, userId, "allocation.confirmed", nameof(Allocation), allocation.Id,
                    $"Confirmed allocation #{allocation.Id} of payout #{payout.Id}: {allocation.Total} by policy v{allocation.PolicyVersion} ("
                    + string.Join(", ", allocation.Lines.Select(l => $"{l.FundName} {l.Total}")) + ").",
                    new
                    {
                        allocation.PayoutId, allocation.PolicyVersion, allocation.SourceAccountId, Currency = allocation.Currency.Code, allocation.Amount,
                        Lines = allocation.Lines.Select(l => new { l.FundId, l.FundName, l.Percentage, l.Amount })
                    }));
                await _repository.SaveChangesAsync();
                return true;
            });
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == AllocationKeyIndex)
        {
            var winner = await _repository.GetAllocationByIdempotencyKeyAsync(key)
                ?? throw new ConflictException("The allocation could not be recorded. Please retry.");
            return await ReplayAsync(winner, hash);
        }
        catch (UniqueConstraintViolationException ex) when (ex.IndexName == OneActiveAllocationIndex)
        {
            throw new ConflictException($"Payout #{payout.Id} has already been allocated.");
        }
        return new AllocationResult(await ToDtoAsync(allocation), Replayed: false);
    }

    public async Task<IReadOnlyList<AllocationDto>> ListAsync()
    {
        var allocations = await _repository.GetAllocationsAsync();
        var context = await ContextAsync(allocations);
        return allocations.OrderByDescending(a => a.ConfirmedAt).Select(a => ToDto(a, context)).ToList();
    }

    public async Task<AllocationDto> GetAsync(int id) => await ToDtoAsync(await AllocationAsync(id));

    public async Task<AllocationResult> SettleAsync(int allocationId, int lineId, SettleLineRequest request, string? idempotencyKey, int userId)
    {
        Allocation? allocation = null;
        AllocationLine? line = null;
        Money? amount = null;

        var (transaction, replayed) = await _posting.PostAsync(new { allocationId, lineId, request }, idempotencyKey, userId,
            async recording =>
            {
                allocation = await AllocationAsync(allocationId);
                line = allocation.Line(lineId);
                var reversed = await ReversedAsync([allocation]);

                var source = await AccountAsync(allocation.SourceAccountId);
                var destination = request.ToAccountId is { } toId ? await AccountAsync(toId) : null;
                var funds = await FundsAsync();
                var readModel = await FinanceReadModel.LoadAsync(_repository);

                amount = Money.Of(request.Amount!.Value, allocation.Currency);
                var fee = request.Fee is not { } f ? null
                    : Money.Of(f, request.FeeChargedTo == FeeChargedTo.Receiving && destination is not null ? destination.Currency : source.Currency);
                var details = new SettlementRequestDetails(
                    request.Method!.Value, amount, request.Date!.Value, request.Description, request.Reference,
                    request.AmountReceived is { } received && destination is not null ? Money.Of(received, destination.Currency) : null,
                    fee, request.QuotedRate);

                return allocation.BuildSettlement(line, details, source, funds[Fund.UnallocatedId], funds[line.FundId], destination,
                    readModel.Position(source, Fund.UnallocatedId), reversed, recording);
            },
            transaction =>
            {
                allocation!.RecordSettlement(line!, transaction, request.Method!.Value, amount!, Now);
                _repository.Add(AuditLog.Event(Now, userId, "allocation.settled", nameof(Allocation), allocation.Id,
                    $"Settled {amount} of allocation #{allocation.Id} for {line!.FundName} by {request.Method.Value.ToString().ToLowerInvariant()} (transaction #{transaction.Id}).",
                    new { LineId = line.Id, line.FundId, Method = request.Method.Value.ToString(), amount!.Amount, TransactionId = transaction.Id }));
                return Task.CompletedTask;
            });

        if (replayed)
            allocation = await _repository.GetAllocationBySettlementTransactionAsync(transaction.Id)
                ?? throw new ConflictException("That request recorded a different kind of transaction.");
        return new AllocationResult(await ToDtoAsync(allocation!), replayed);
    }

    public async Task<AllocationDto> CancelAsync(int id, CancelAllocationRequest request, int userId)
    {
        var allocation = await AllocationAsync(id);
        if (allocation.IsCancelled) return await ToDtoAsync(allocation);

        allocation.Cancel(request.Reason, Now, await ReversedAsync([allocation]));
        _repository.Add(AuditLog.Event(Now, userId, "allocation.cancelled", nameof(Allocation), allocation.Id,
            $"Cancelled allocation #{allocation.Id} of payout #{allocation.PayoutId}: {allocation.CancellationReason}"));
        await _repository.SaveChangesAsync();
        return await ToDtoAsync(allocation);
    }

    // ---- Helpers ----

    private async Task EnsureNotAllocatedAsync(Payout payout)
    {
        if (await _repository.GetActiveAllocationForPayoutAsync(payout.Id) is { } existing)
            throw new ConflictException($"Payout #{payout.Id} has already been allocated (allocation #{existing.Id}).");
    }

    private async Task<AllocationPolicy> PolicyAsync(int? id) => id is { } policyId
        ? await _repository.GetPolicyAsync(policyId) ?? throw new DomainException($"Allocation policy {policyId} does not exist.")
        : await _repository.GetActivePolicyAsync() ?? throw new DomainException("No allocation policy is active.");

    private async Task<Payout> PayoutAsync(int id) =>
        await _repository.GetPayoutAsync(id) ?? throw new DomainException($"Payout {id} does not exist.");

    private async Task<Allocation> AllocationAsync(int id) =>
        await _repository.GetAllocationAsync(id) ?? throw new NotFoundException($"Allocation {id} does not exist.");

    private async Task<FinancialAccount> AccountAsync(int id) =>
        await _repository.GetAccountAsync(id) ?? throw new DomainException($"Account {id} does not exist.");

    private async Task<Dictionary<int, Fund>> FundsAsync() => (await _repository.GetFundsAsync()).ToDictionary(f => f.Id);

    private async Task<HashSet<int>> ReversedAsync(IEnumerable<Allocation> allocations)
    {
        var ids = allocations.SelectMany(a => a.Lines).SelectMany(l => l.Settlements).Select(s => s.LedgerTransactionId).ToList();
        return ids.Count == 0 ? [] : (await _repository.GetReversalIdsAsync(ids)).Keys.ToHashSet();
    }

    private async Task<AllocationResult> ReplayAsync(Allocation previous, string hash) =>
        previous.RequestHash == hash
            ? new AllocationResult(await ToDtoAsync(previous), Replayed: true)
            : throw new ConflictException(Idempotency.KeyReusedMessage);

    private record DtoContext(HashSet<int> Reversed, Dictionary<int, FinancialAccount> Accounts, Dictionary<int, Payout> Payouts);

    private async Task<DtoContext> ContextAsync(IReadOnlyCollection<Allocation> allocations) => new(
        await ReversedAsync(allocations),
        (await _repository.GetAccountsAsync()).ToDictionary(a => a.Id),
        (await _repository.GetPayoutsAsync()).ToDictionary(p => p.Id));

    private async Task<AllocationDto> ToDtoAsync(Allocation allocation) => ToDto(allocation, await ContextAsync([allocation]));

    private static AllocationDto ToDto(Allocation a, DtoContext context)
    {
        var lines = a.Lines.OrderBy(l => l.Position).Select(l => new AllocationLineDto(
            l.Id, l.FundId, l.FundName, l.Percentage, MoneyDto.From(l.Total),
            MoneyDto.From(l.Settled(context.Reversed)), MoneyDto.From(l.Outstanding(context.Reversed)),
            l.Settlements.OrderBy(s => s.RecordedAt).Select(s => new SettlementDto(
                s.Id, s.LedgerTransactionId, s.Method, MoneyDto.From(s.Money), s.RecordedAt,
                context.Reversed.Contains(s.LedgerTransactionId))).ToList())).ToList();

        var settled = Money.Sum(a.Lines.Select(l => l.Settled(context.Reversed)), a.Currency);
        return new AllocationDto(
            a.Id, a.PayoutId, context.Payouts.TryGetValue(a.PayoutId, out var payout) ? payout.Source : $"Payout {a.PayoutId}",
            a.AllocationPolicyId, a.PolicyVersion,
            a.SourceAccountId, context.Accounts.TryGetValue(a.SourceAccountId, out var account) ? account.Name : $"Account {a.SourceAccountId}",
            a.Status(context.Reversed), MoneyDto.From(a.Total), MoneyDto.From(settled),
            MoneyDto.From(a.IsCancelled ? Money.Zero(a.Currency) : a.Total - settled),
            a.ConfirmedAt, a.CancelledAt, a.CancellationReason, lines);
    }

    private static AllocationPolicyDto ToDto(AllocationPolicy p, IReadOnlyDictionary<int, Fund> funds) => new(
        p.Id, p.Version, p.IsActive, p.CreatedAt, p.ActivatedAt,
        p.Lines.OrderBy(l => l.Position).Select(l => new PolicyLineDto(l.FundId, FundName(funds, l.FundId), l.Percentage)).ToList());

    private static string FundName(IReadOnlyDictionary<int, Fund> funds, int id) => funds.TryGetValue(id, out var f) ? f.Name : $"Fund {id}";

    private static string Describe(AllocationPolicy policy, IReadOnlyDictionary<int, Fund> funds) =>
        string.Join(" / ", policy.Lines.OrderBy(l => l.Position).Select(l => $"{FundName(funds, l.FundId)} {l.Percentage:0.####}%"));
}
