using MelamuHoldings.Domain.Common;

namespace MelamuHoldings.Domain.Finance;

// How the business measures continuity (PLAN.md D6–D8): which fund holds the reserve for setbacks, how many months of
// essential expenses it should cover, the owner's monthly essential expense, and which separate fund holds the capital to
// restore trading. There is one settings record. The restoration target is that fund's own target (Fund.Target), so no
// amount is stored twice, and the two funds are different, so no money counts towards both.
public class ContinuitySettings
{
    public const int SingletonId = 1;
    public const int DefaultMonths = 24;
    public const int MaxMonths = 120;

    public int Id { get; private set; } = SingletonId;
    public int ContinuityFundId { get; private set; }
    public int Months { get; private set; } = DefaultMonths;

    // The currency of the essential expense and so of the continuity target (D7: ZAR)
    public Currency Currency { get; private set; } = Currency.ZAR;

    // Entered by the owner (D8); none until it is
    public decimal? MonthlyEssentialExpense { get; private set; }
    public int? RestorationFundId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private ContinuitySettings() { }

    public ContinuitySettings(int continuityFundId)
    {
        ContinuityFundId = continuityFundId;
    }

    public void Update(Fund continuityFund, int months, decimal? monthlyEssentialExpense, Fund? restorationFund, DateTime now)
    {
        if (continuityFund.IsSystem || restorationFund?.IsSystem == true)
            throw new DomainException("The Unallocated fund cannot hold the continuity reserve or restoration capital.");
        if (restorationFund is not null && restorationFund.Id == continuityFund.Id)
            throw new DomainException("Continuity and restoration must be different funds, so no money counts towards both.");
        if (months is < 1 or > MaxMonths) throw new DomainException($"Choose between 1 and {MaxMonths} months of essential expenses.");
        if (monthlyEssentialExpense is <= 0) throw new DomainException("The monthly essential expense must be more than zero.");
        var expense = monthlyEssentialExpense is { } amount ? Money.Of(amount, Currency).Amount : (decimal?)null;   // whole minor units

        ContinuityFundId = continuityFund.Id;
        Months = months;
        MonthlyEssentialExpense = expense;
        RestorationFundId = restorationFund?.Id;
        UpdatedAt = now;
    }

    // Months × monthly essential expense, when the expense is set
    public decimal? Target => MonthlyEssentialExpense * Months;

    // Counted money ÷ the monthly expense, rounded down to one decimal so it never overstates; never below zero
    public static decimal CoverageMonths(decimal counted, decimal monthlyExpense) =>
        Math.Max(0, Math.Round(counted / monthlyExpense, 1, MidpointRounding.ToZero));

    // What is still missing; never below zero
    public static decimal Shortfall(decimal target, decimal counted) => Math.Max(0, target - counted);

    // Percentage of the target held, rounded down to one decimal, between 0 and 100
    public static decimal Progress(decimal counted, decimal target) =>
        Math.Clamp(Math.Round(counted / target * 100, 1, MidpointRounding.ToZero), 0, 100);
}
