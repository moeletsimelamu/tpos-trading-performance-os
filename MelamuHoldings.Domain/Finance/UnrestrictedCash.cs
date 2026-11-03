namespace MelamuHoldings.Domain.Finance;

// How much cash is free to use, in one currency (PLAN.md D9): Held is Unallocated and Operating Capital in company bank and
// cash accounts, less Operating Capital's minimum, what confirmed allocations still owe other funds, received payouts not
// allocated yet, and committed purchases. It can go below zero, when more is spoken for than is held.
public sealed record UnrestrictedCash(decimal Held, decimal OperatingMinimum, decimal OwedToOtherFunds, decimal PayoutsNotAllocated, decimal CommittedPurchases)
{
    public decimal Amount => Held - OperatingMinimum - OwedToOtherFunds - PayoutsNotAllocated - CommittedPurchases;

    // Obligations are shown beside the figure and not deducted: a bill can still be in the balance's expenses, unlinked
    public decimal AfterObligations(decimal obligationsDue) => Amount - obligationsDue;

    public static bool CountsFund(int fundId) => fundId is Fund.UnallocatedId or Fund.OperatingCapitalId;

    // Company money only: a personal account, or one whose ownership is not stated, is listed apart
    public static bool CountsAccount(FinancialAccountType type, Ownership ownership) =>
        type is FinancialAccountType.Bank or FinancialAccountType.Cash && ownership == Ownership.Company;
}
