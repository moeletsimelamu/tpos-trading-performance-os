using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

public enum PropAccountFeeKind { Purchase, Reset, Monthly, Other }

// An expense already in the ledger, named as a cost of one prop firm account. It is only a link: the money moved when
// the expense was recorded, and removing the link leaves the expense where it is.
public class PropAccountFee
{
    public int Id { get; private set; }
    public int PropFirmAccountId { get; private set; }
    public int LedgerTransactionId { get; private set; }
    public PropAccountFeeKind Kind { get; private set; }
    public DateTime LinkedAt { get; private set; }
    public int LinkedByUserId { get; private set; }

    private PropAccountFee() { }

    public PropAccountFee(int propFirmAccountId, PropAccountFeeKind kind, LedgerTransaction expense, bool isReversed, int userId, DateTime now)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("Choose what the fee was for.");
        if (expense.Type != LedgerTransactionType.Expense) throw new DomainException("Only an expense can be linked as a fee.");
        if (isReversed) throw new DomainException($"Transaction {expense.Id} has been reversed, so it did not cost anything.");

        PropFirmAccountId = propFirmAccountId;
        LedgerTransactionId = expense.Id;
        Kind = kind;
        LinkedAt = now;
        LinkedByUserId = userId;
    }
}

public enum PropAccountOutcome { FeesWithoutPayouts, PayoutsReceived, NothingLinked }

public record PropFeeAmount(Currency Currency, decimal Amount, bool Reversed);

// One currency's money for an account. Nothing is converted: a fee in USD and a payout credited in ZAR are two lines.
public record PropAccountCurrencyLine(Currency Currency, decimal Fees, decimal PayoutsReceived)
{
    public decimal Net => PayoutsReceived - Fees;
}

// What a prop firm account has cost and paid back. Only money that actually moved counts: a reversed fee does not, and
// a payout counts once received, for what reached the account. A payout still on its way is kept apart in Awaiting.
public record PropAccountEconomics(
    PropAccountOutcome Outcome, IReadOnlyList<PropAccountCurrencyLine> Lines, IReadOnlyList<Money> Awaiting, int FeesCounted, int PayoutsCounted)
{
    public static PropAccountEconomics Calculate(IEnumerable<PropFeeAmount> fees, IEnumerable<Payout> payouts)
    {
        var paid = fees.Where(f => !f.Reversed).ToList();
        var linked = payouts.ToList();
        var received = linked.Where(p => p.Status == PayoutStatus.Received && p.Credited is not null).ToList();

        var lines = paid.Select(f => new PropAccountCurrencyLine(f.Currency, f.Amount, 0m))
            .Concat(received.Select(p => new PropAccountCurrencyLine(p.Credited!.Currency, 0m, p.Credited.Amount)));
        var awaiting = linked.Where(p => p.Status is PayoutStatus.Requested or PayoutStatus.Approved)
            .Select(p => p.Approved ?? p.Requested)
            .GroupBy(m => m.Currency.Code)
            .OrderBy(g => g.Key)
            .Select(g => Money.Of(g.Sum(m => m.Amount), g.First().Currency))
            .ToList();

        var outcome = received.Count > 0 ? PropAccountOutcome.PayoutsReceived
            : paid.Count > 0 ? PropAccountOutcome.FeesWithoutPayouts
            : PropAccountOutcome.NothingLinked;
        return new PropAccountEconomics(outcome, Combine(lines), awaiting, paid.Count, received.Count);
    }

    // All accounts together, still one line per currency
    public static IReadOnlyList<PropAccountCurrencyLine> Totals(IEnumerable<PropAccountEconomics> accounts) =>
        Combine(accounts.SelectMany(a => a.Lines));

    private static List<PropAccountCurrencyLine> Combine(IEnumerable<PropAccountCurrencyLine> lines) =>
        lines.GroupBy(l => l.Currency.Code)
            .OrderBy(g => g.Key)
            .Select(g => new PropAccountCurrencyLine(g.First().Currency, g.Sum(l => l.Fees), g.Sum(l => l.PayoutsReceived)))
            .ToList();
}
