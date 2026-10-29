using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

// Confirmed → PartiallySettled → Reconciled is derived from the settlements, never set by hand; Cancelled is stored
public enum AllocationStatus { Confirmed, PartiallySettled, Reconciled, Cancelled }

// How a line is settled: an earmark inside the receiving account, a transfer to another account in the same
// currency, or a currency exchange into an account in another currency
public enum SettlementMethod { Earmark, Transfer, Exchange }

// The confirmed split of one received payout. Lines keep a snapshot of the fund name, percentage and amount,
// so a later policy change or fund rename never alters what was confirmed. Confirming moves no money: each line
// is settled by a real ledger transaction (earmark, transfer or exchange) out of the Unallocated fund of the
// account the payout was received into.
public class Allocation
{
    public const int MaxReasonLength = 200;

    private readonly List<AllocationLine> _lines = new();

    public int Id { get; private set; }
    public int PayoutId { get; private set; }
    public int AllocationPolicyId { get; private set; }
    public int PolicyVersion { get; private set; }

    // The account holding the received money, and the amount and currency being allocated
    public int SourceAccountId { get; private set; }
    public Currency Currency { get; private set; } = Currency.USD;
    public decimal Amount { get; private set; }

    public DateTime ConfirmedAt { get; private set; }
    public int ConfirmedByUserId { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    // Touched by every settlement, so concurrent settlements of one allocation conflict on RowVersion
    public DateTime? LastSettledAt { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<AllocationLine> Lines => _lines;
    public Money Total => Money.Of(Amount, Currency);
    public bool IsCancelled => CancelledAt is not null;

    private Allocation() { }

    public static Allocation Confirm(Payout payout, AllocationPolicy policy, IReadOnlyDictionary<int, Fund> funds, RecordingInfo recording)
    {
        if (payout.Status != PayoutStatus.Received || payout.Credited is not { } credited || payout.ReceivedIntoAccountId is not { } accountId)
            throw new DomainException("Only a received payout can be allocated: until the money has arrived there is nothing to split.");

        var allocation = new Allocation
        {
            PayoutId = payout.Id,
            AllocationPolicyId = policy.Id,
            PolicyVersion = policy.Version,
            SourceAccountId = accountId,
            Currency = credited.Currency,
            Amount = credited.Amount,
            ConfirmedAt = recording.RecordedAt,
            ConfirmedByUserId = recording.UserId,
            IdempotencyKey = recording.IdempotencyKey,
            RequestHash = recording.RequestHash
        };

        var lines = policy.Lines.OrderBy(l => l.Position).ToList();
        var shares = policy.Split(credited);
        for (var i = 0; i < lines.Count; i++)
        {
            var fund = funds.TryGetValue(lines[i].FundId, out var f) ? f : throw new DomainException($"Fund {lines[i].FundId} does not exist.");
            allocation._lines.Add(new AllocationLine(fund, lines[i].Percentage, shares[i], lines[i].Position));
        }
        return allocation;
    }

    // `reversedTransactionIds` are ledger transactions that have been reversed: their settlements no longer count
    public AllocationStatus Status(IReadOnlySet<int> reversedTransactionIds)
    {
        if (IsCancelled) return AllocationStatus.Cancelled;
        var settled = _lines.Sum(l => l.Settled(reversedTransactionIds).Amount);
        if (settled == 0m) return AllocationStatus.Confirmed;
        return _lines.All(l => l.Outstanding(reversedTransactionIds).IsZero) ? AllocationStatus.Reconciled : AllocationStatus.PartiallySettled;
    }

    public AllocationLine Line(int lineId) =>
        _lines.SingleOrDefault(l => l.Id == lineId) ?? throw new DomainException($"Allocation {Id} has no line {lineId}.");

    // Builds the ledger transaction that settles part or all of one line. The caller saves it, then calls RecordSettlement.
    // `unallocatedInSource` is the current balance of the Unallocated fund in the source account.
    public LedgerTransaction BuildSettlement(
        AllocationLine line, SettlementRequestDetails request, FinancialAccount source, Fund unallocated, Fund lineFund,
        FinancialAccount? destination, Money unallocatedInSource, IReadOnlySet<int> reversedTransactionIds, RecordingInfo recording)
    {
        if (IsCancelled) throw new DomainException("A cancelled allocation cannot be settled.");
        if (source.Id != SourceAccountId) throw new DomainException("Settlements come out of the account the payout was received into.");
        if (lineFund.Id != line.FundId) throw new DomainException("The fund does not match the allocation line.");
        if (request.Amount.Currency != Currency) throw new CurrencyMismatchException(Currency, request.Amount.Currency);
        if (!request.Amount.IsPositive) throw new DomainException("The settlement amount must be greater than zero.");

        var outstanding = line.Outstanding(reversedTransactionIds);
        if (request.Amount > outstanding)
            throw new DomainException($"{request.Amount} is more than the {outstanding} still to settle for '{line.FundName}'.");

        // A fee charged to the source account comes out of the same Unallocated money
        var needed = request.Fee is { } fee && fee.Currency == source.Currency ? request.Amount + fee : request.Amount;
        if (unallocatedInSource < needed)
            throw new DomainException($"'{source.Name}' holds only {unallocatedInSource} unallocated, less than the {needed} needed.");

        var details = new TransactionDetails(request.Date,
            request.Description ?? $"Allocation #{Id}: {line.FundName} {line.Percentage:0.####}%", request.Reference, "Allocation");

        return request.Method switch
        {
            SettlementMethod.Earmark => destination is null || destination.Id == source.Id
                ? LedgerTransaction.FundReallocation(source, unallocated, lineFund, request.Amount, details, recording)
                : throw new DomainException("An earmark keeps the money in the receiving account. Use a transfer to move it."),
            SettlementMethod.Transfer => LedgerTransaction.Transfer(
                source, unallocated, destination ?? throw new DomainException("Choose the account to transfer to."), lineFund,
                request.Amount, details, recording),
            SettlementMethod.Exchange => LedgerTransaction.CurrencyExchange(
                source, unallocated, request.Amount,
                destination ?? throw new DomainException("Choose the account the converted money went to."), lineFund,
                request.AmountReceived ?? throw new DomainException("Enter the amount received after conversion."),
                details, recording, request.Fee, request.QuotedRate),
            _ => throw new DomainException("Unknown settlement method.")
        };
    }

    public void RecordSettlement(AllocationLine line, LedgerTransaction transaction, SettlementMethod method, Money amount, DateTime now)
    {
        if (transaction.Id == 0) throw new DomainException("A settlement links to a saved ledger transaction.");
        line.AddSettlement(transaction.Id, method, amount, now);
        LastSettledAt = now;
    }

    public void Cancel(string reason, DateTime now, IReadOnlySet<int> reversedTransactionIds)
    {
        if (IsCancelled) throw new DomainException("This allocation is already cancelled.");
        if (_lines.Any(l => l.Settled(reversedTransactionIds).IsPositive))
            throw new DomainException("An allocation with settlements cannot be cancelled. Reverse the settlement transactions first.");
        CancellationReason = Text.Required(reason, MaxReasonLength, "Reason");
        CancelledAt = now;
    }
}

// What the operator entered to settle a line. AmountReceived, Fee and QuotedRate apply to exchanges
// (a fee may also be charged on a transfer, but is then recorded as its own expense).
public record SettlementRequestDetails(
    SettlementMethod Method, Money Amount, DateOnly Date, string? Description, string? Reference,
    Money? AmountReceived = null, Money? Fee = null, decimal? QuotedRate = null);

public class AllocationLine
{
    private readonly List<AllocationSettlement> _settlements = new();

    public int Id { get; private set; }
    public int AllocationId { get; private set; }
    public int FundId { get; private set; }

    // Snapshot at confirmation: what the line was for and how it was calculated
    public string FundName { get; private set; } = string.Empty;
    public decimal Percentage { get; private set; }
    public Currency Currency { get; private set; } = Currency.USD;
    public decimal Amount { get; private set; }
    public int Position { get; private set; }

    public IReadOnlyList<AllocationSettlement> Settlements => _settlements;
    public Money Total => Money.Of(Amount, Currency);

    private AllocationLine() { }

    internal AllocationLine(Fund fund, decimal percentage, Money amount, int position)
    {
        FundId = fund.Id;
        FundName = fund.Name;
        Percentage = percentage;
        Currency = amount.Currency;
        Amount = amount.Amount;
        Position = position;
    }

    // Settlements whose ledger transaction was reversed no longer count
    public Money Settled(IReadOnlySet<int> reversedTransactionIds) => Money.Sum(
        _settlements.Where(s => !reversedTransactionIds.Contains(s.LedgerTransactionId)).Select(s => s.Money), Currency);

    public Money Outstanding(IReadOnlySet<int> reversedTransactionIds) => Total - Settled(reversedTransactionIds);

    internal void AddSettlement(int transactionId, SettlementMethod method, Money amount, DateTime now) =>
        _settlements.Add(new AllocationSettlement(transactionId, method, amount, now));
}

// Links a ledger transaction to the allocation line it settles, and how much of the line it covers
public class AllocationSettlement
{
    public int Id { get; private set; }
    public int AllocationLineId { get; private set; }
    public int LedgerTransactionId { get; private set; }
    public SettlementMethod Method { get; private set; }
    public Currency Currency { get; private set; } = Currency.USD;
    public decimal Amount { get; private set; }
    public DateTime RecordedAt { get; private set; }

    public Money Money => Money.Of(Amount, Currency);

    private AllocationSettlement() { }

    internal AllocationSettlement(int transactionId, SettlementMethod method, Money amount, DateTime recordedAt)
    {
        LedgerTransactionId = transactionId;
        Method = method;
        Currency = amount.Currency;
        Amount = amount.Amount;
        RecordedAt = recordedAt;
    }
}
