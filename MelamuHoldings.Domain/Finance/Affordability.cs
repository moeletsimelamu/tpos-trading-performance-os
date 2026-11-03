namespace MelamuHoldings.Domain.Finance;

// Affordable: the fund stays at or above its minimum. BreachesMinimum: it would not. NoMinimum: the money is there, but the
// fund has no minimum in this currency to compare with. NotEnoughMoney: no minimum in this currency, and the fund would go below zero.
public enum AffordabilityVerdict { Affordable, BreachesMinimum, NoMinimum, NotEnoughMoney }

// Can a planned purchase be afforded, from recorded balances: After = available − committed elsewhere − purchase.
// Shortfall is how far After falls below the minimum, or below zero with no minimum. All amounts are in one currency.
public sealed record Affordability(AffordabilityVerdict Verdict, decimal After, decimal? Shortfall)
{
    public static Affordability Evaluate(decimal available, decimal committedElsewhere, decimal purchase, decimal? minimum)
    {
        var after = available - committedElsewhere - purchase;
        var floor = minimum ?? 0;
        var verdict = minimum is not null
            ? (after >= floor ? AffordabilityVerdict.Affordable : AffordabilityVerdict.BreachesMinimum)
            : (after >= 0 ? AffordabilityVerdict.NoMinimum : AffordabilityVerdict.NotEnoughMoney);
        return new Affordability(verdict, after, after < floor ? floor - after : null);
    }
}
