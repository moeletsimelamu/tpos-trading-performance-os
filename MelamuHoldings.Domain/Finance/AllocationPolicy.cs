using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

// How received money is split between funds: a numbered version of (fund, percentage) lines summing to exactly 100.
// A version is never edited. Changing the split means creating a new version, so every confirmed allocation
// can always be traced to the exact rule that produced it.
public class AllocationPolicy
{
    public const int MaxPercentageDecimals = 4;

    private readonly List<AllocationPolicyLine> _lines = new();

    public int Id { get; private set; }
    public int Version { get; private set; }

    // Exactly one version is active; new allocations use it unless another version is named
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Null for the default policy seeded with the database
    public int? CreatedByUserId { get; private set; }
    public DateTime? ActivatedAt { get; private set; }

    public IReadOnlyList<AllocationPolicyLine> Lines => _lines;

    private AllocationPolicy() { }

    public AllocationPolicy(int version, IReadOnlyList<(Fund Fund, decimal Percentage)> lines, DateTime createdAt, int userId)
    {
        if (version < 1) throw new DomainException("Policy versions start at 1.");
        if (lines.Count == 0) throw new DomainException("A policy needs at least one fund.");

        foreach (var (fund, percentage) in lines)
        {
            if (fund.Id == Fund.UnallocatedId)
                throw new DomainException("The Unallocated fund cannot be part of a policy: allocating is how money leaves it.");
            if (percentage <= 0 || percentage > 100)
                throw new DomainException($"The percentage for '{fund.Name}' must be greater than 0 and at most 100.");
            if (decimal.Round(percentage, MaxPercentageDecimals) != percentage)
                throw new DomainException($"Percentages can have at most {MaxPercentageDecimals} decimal places.");
        }
        if (lines.Select(l => l.Fund.Id).Distinct().Count() != lines.Count)
            throw new DomainException("Each fund can appear only once in a policy.");

        var total = lines.Sum(l => l.Percentage);
        if (total != 100m)
            throw new DomainException($"The percentages add up to {total:0.####}%. They must add up to exactly 100%.");

        Version = version;
        CreatedAt = createdAt;
        CreatedByUserId = userId;
        for (var i = 0; i < lines.Count; i++)
            _lines.Add(new AllocationPolicyLine(lines[i].Fund.Id, lines[i].Percentage, i + 1));
    }

    public void Activate(DateTime now)
    {
        IsActive = true;
        ActivatedAt = now;
    }

    public void Deactivate() => IsActive = false;

    // Splits an amount by this policy's percentages, in policy line order
    public IReadOnlyList<Money> Split(Money amount) =>
        AllocationCalculator.Split(amount, _lines.OrderBy(l => l.Position).Select(l => l.Percentage).ToList());
}

public class AllocationPolicyLine
{
    public int Id { get; private set; }
    public int AllocationPolicyId { get; private set; }
    public int FundId { get; private set; }
    public decimal Percentage { get; private set; }

    // Line order: also the tie-breaker when rounding leaves spare minor units
    public int Position { get; private set; }

    private AllocationPolicyLine() { }

    internal AllocationPolicyLine(int fundId, decimal percentage, int position)
    {
        FundId = fundId;
        Percentage = percentage;
        Position = position;
    }
}

// Largest-remainder split (PLAN.md §3.4): every share is rounded down to the currency's minor unit, then the
// minor units left over go one at a time to the shares that lost the most in rounding, ties going to the earlier
// line. The shares always add up to the original amount exactly: no cent is created or lost.
public static class AllocationCalculator
{
    public static IReadOnlyList<Money> Split(Money amount, IReadOnlyList<decimal> percentages)
    {
        if (amount.IsNegative) throw new DomainException("Only a positive amount can be allocated.");
        if (percentages.Count == 0) throw new DomainException("There is nothing to split the amount between.");
        if (percentages.Sum() != 100m) throw new DomainException("Allocation percentages must add up to exactly 100%.");

        var unit = amount.Currency.SmallestUnit;
        var units = amount.Amount / unit;  // whole minor units, e.g. 135000 cents

        var raw = percentages.Select(p => units * p / 100m).ToList();
        var floors = raw.Select(decimal.Floor).ToList();
        var spare = (int)(units - floors.Sum());

        var bySizeOfRemainder = raw
            .Select((value, index) => (Index: index, Remainder: value - floors[index]))
            .OrderByDescending(x => x.Remainder).ThenBy(x => x.Index)
            .Take(spare)
            .Select(x => x.Index);
        foreach (var index in bySizeOfRemainder) floors[index] += 1;

        var shares = floors.Select(f => Money.Of(f * unit, amount.Currency)).ToList();
        if (Money.Sum(shares, amount.Currency) != amount)
            throw new DomainException($"Allocation rounding error: the shares do not add up to {amount}.");
        return shares;
    }
}
