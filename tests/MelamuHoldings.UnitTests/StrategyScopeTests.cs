using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.UnitTests;

// Rule categories, where a strategy applies, and its default template
public class StrategyScopeTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static Strategy Saved(Strategy strategy)
    {
        typeof(Strategy).GetProperty(nameof(Strategy.Id))!.SetValue(strategy, 7);
        var id = 200;
        foreach (var rule in strategy.Rules.Where(r => r.Id == 0))
            typeof(StrategyRule).GetProperty(nameof(StrategyRule.Id))!.SetValue(rule, ++id);
        return strategy;
    }

    [Fact]
    public void A_checklist_keeps_the_category_its_rule_had_when_it_started()
    {
        var strategy = new Strategy(1, "ORB", null, Now);
        strategy.SetRules([new RuleDraft(null, "Opening range marked", true, RuleCategory.Setup), new RuleDraft(null, "Stop beyond the range", true, RuleCategory.Risk)], Now);
        Saved(strategy);
        Assert.Equal([RuleCategory.Setup, RuleCategory.Risk], strategy.ActiveRules.Select(r => r.Category));

        var checklist = new TradeChecklist(5, strategy, Now);
        var stop = strategy.ActiveRules[1].Id;
        strategy.SetRules([new RuleDraft(strategy.ActiveRules[0].Id, "Opening range marked", true, RuleCategory.Setup), new RuleDraft(stop, "Stop beyond the range", true, RuleCategory.Exit)], Now.AddDays(1));

        Assert.Equal(RuleCategory.Exit, strategy.ActiveRules[1].Category);
        Assert.Equal(RuleCategory.Risk, checklist.Items.Single(i => i.StrategyRuleId == stop).Category);
        Assert.Throws<DomainException>(() => strategy.SetRules([new RuleDraft(null, "Odd", true, (RuleCategory)42)], Now));
    }

    [Fact]
    public void Instruments_and_sessions_are_trimmed_kept_once_and_an_empty_list_means_anywhere()
    {
        var strategy = new Strategy(1, "ORB", null, Now);
        Assert.True(strategy.CoversInstrument("GC") && strategy.CoversSession("Asia"));

        strategy.SetScope([" NQ", "es", "nq", " "], ["New York"], Now);

        Assert.Equal(["NQ", "es"], strategy.Instruments);
        Assert.Equal(["New York"], strategy.Sessions);
        Assert.True(strategy.CoversInstrument(" ES "));
        Assert.False(strategy.CoversInstrument("GC"));
        Assert.True(strategy.CoversSession("new york"));
        Assert.False(strategy.CoversSession("London"));
        Assert.False(strategy.CoversSession(null));

        Assert.Throws<DomainException>(() => strategy.SetScope([new string('X', Strategy.MaxScopeItemLength + 1)], [], Now));
        Assert.Throws<DomainException>(() => strategy.SetScope(Enumerable.Range(0, Strategy.MaxScopeItems + 1).Select(i => $"I{i}"), [], Now));
        Assert.Equal(["NQ", "es"], strategy.Instruments);   // a refused change leaves the scope as it was
    }
}
