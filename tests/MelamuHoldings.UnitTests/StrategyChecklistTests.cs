using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.UnitTests;

public class StrategyChecklistTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static int _nextRuleId = 100;

    // The database gives rules their ids; here they are given as if saved
    private static Strategy Saved(Strategy strategy, int id = 7)
    {
        typeof(Strategy).GetProperty(nameof(Strategy.Id))!.SetValue(strategy, id);
        foreach (var rule in strategy.Rules.Where(r => r.Id == 0))
            typeof(StrategyRule).GetProperty(nameof(StrategyRule.Id))!.SetValue(rule, Interlocked.Increment(ref _nextRuleId));
        return strategy;
    }

    private static Strategy Orb(params RuleDraft[] rules)
    {
        var strategy = new Strategy(1, "ORB", null, Now);
        strategy.SetRules(rules.Length > 0 ? rules :
        [
            new RuleDraft(null, "Stop loss is set", true),
            new RuleDraft(null, "Risk within 1%", true),
            new RuleDraft(null, "News checked", false)
        ], Now);
        return Saved(strategy);
    }

    private static int RuleId(Strategy s, string text) => s.Rules.Single(r => r.Text == text).Id;

    [Fact]
    public void Rules_are_edited_in_place_retired_when_left_out_and_restored_at_the_end()
    {
        var strategy = Orb();
        var stop = RuleId(strategy, "Stop loss is set");
        var news = RuleId(strategy, "News checked");

        strategy.SetRules([new RuleDraft(news, "News calendar checked", true), new RuleDraft(stop, "Stop loss is set", true)], Now.AddDays(1));

        Assert.Equal(["News calendar checked", "Stop loss is set"], strategy.ActiveRules.Select(r => r.Text));
        Assert.True(strategy.Rules.Single(r => r.Id == news).IsMandatory);
        var retired = strategy.Rules.Single(r => r.Text == "Risk within 1%");
        Assert.Equal(Now.AddDays(1), retired.RetiredAt);

        strategy.RestoreRule(retired.Id, Now.AddDays(2));
        Assert.Equal("Risk within 1%", strategy.ActiveRules.Last().Text);
        Assert.Null(retired.RetiredAt);
    }

    [Fact]
    public void Rules_must_be_named_once_and_belong_to_the_strategy()
    {
        var strategy = Orb();

        Assert.Throws<DomainException>(() => strategy.SetRules([new RuleDraft(null, "Same", true), new RuleDraft(null, "same", false)], Now));
        Assert.Throws<DomainException>(() => strategy.SetRules([new RuleDraft(null, "  ", true)], Now));
        Assert.Throws<DomainException>(() => strategy.SetRules([new RuleDraft(9999, "Not mine", true)], Now));
        Assert.Throws<DomainException>(() => strategy.SetRules(
            Enumerable.Range(1, Strategy.MaxActiveRules + 1).Select(i => new RuleDraft(null, $"Rule {i}", false)).ToList(), Now));
        Assert.Throws<DomainException>(() => new Strategy(1, " ", null, Now));
    }

    [Fact]
    public void A_checklist_copies_the_active_rules_so_later_edits_do_not_change_it()
    {
        var strategy = Orb();
        var checklist = new TradeChecklist(5, strategy, Now);
        var stop = RuleId(strategy, "Stop loss is set");

        strategy.SetRules([new RuleDraft(stop, "Stop placed beyond the range", false)], Now.AddDays(1));

        Assert.Equal(["Stop loss is set", "Risk within 1%", "News checked"], checklist.Items.Select(i => i.Text));
        Assert.True(checklist.Items.First().IsMandatory);
        Assert.Throws<DomainException>(() => new TradeChecklist(5, Saved(new Strategy(1, "Empty", null, Now), 8), Now));
    }

    [Fact]
    public void Only_a_mandatory_rule_not_met_is_a_violation_and_compliance_needs_every_answer()
    {
        var strategy = Orb();
        var checklist = new TradeChecklist(5, strategy, Now);
        int Id(string t) => RuleId(strategy, t);

        checklist.Answer(new Dictionary<int, RuleAnswer> { [Id("Stop loss is set")] = RuleAnswer.Met, [Id("News checked")] = RuleAnswer.NotMet }, Now);
        Assert.False(checklist.IsComplete);
        Assert.Null(checklist.IsCompliant);   // not known until every rule is answered

        checklist.Answer(new Dictionary<int, RuleAnswer> { [Id("Risk within 1%")] = RuleAnswer.Met }, Now);
        Assert.True(checklist.IsCompliant);   // an unmet optional rule is not a violation

        checklist.Answer(new Dictionary<int, RuleAnswer> { [Id("Risk within 1%")] = RuleAnswer.NotMet }, Now);
        Assert.False(checklist.IsCompliant);
        Assert.Equal(["Risk within 1%"], checklist.Violations.Select(v => v.Text));
    }

    [Fact]
    public void Answers_are_appended_not_overwritten_and_unchanged_answers_add_nothing()
    {
        var strategy = Orb();
        var checklist = new TradeChecklist(5, strategy, Now);
        var risk = RuleId(strategy, "Risk within 1%");

        Assert.Equal(1, checklist.Answer(new Dictionary<int, RuleAnswer> { [risk] = RuleAnswer.Met }, Now));
        Assert.Equal(0, checklist.Answer(new Dictionary<int, RuleAnswer> { [risk] = RuleAnswer.Met }, Now.AddMinutes(1)));
        Assert.Equal(1, checklist.Answer(new Dictionary<int, RuleAnswer> { [risk] = RuleAnswer.NotMet }, Now.AddMinutes(2)));

        var item = checklist.Items.Single(i => i.StrategyRuleId == risk);
        Assert.Equal([(1, RuleAnswer.Met), (2, RuleAnswer.NotMet)], item.Responses.Select(r => (r.Sequence, r.Answer)));
        Assert.Equal(RuleAnswer.NotMet, item.Current!.Answer);
    }

    [Fact]
    public void Not_applicable_is_only_for_optional_rules_and_answers_must_be_on_the_checklist()
    {
        var strategy = Orb();
        var checklist = new TradeChecklist(5, strategy, Now);

        Assert.Throws<DomainException>(() => checklist.Answer(new Dictionary<int, RuleAnswer> { [RuleId(strategy, "Stop loss is set")] = RuleAnswer.NotApplicable }, Now));
        Assert.Throws<DomainException>(() => checklist.Answer(new Dictionary<int, RuleAnswer> { [424242] = RuleAnswer.Met }, Now));
        Assert.Equal(1, checklist.Answer(new Dictionary<int, RuleAnswer> { [RuleId(strategy, "News checked")] = RuleAnswer.NotApplicable }, Now));

        // A rule added after the checklist started is not on it
        strategy.SetRules([.. strategy.ActiveRules.Select(r => new RuleDraft(r.Id, r.Text, r.IsMandatory)), new RuleDraft(null, "Later rule", true)], Now);
        Saved(strategy);
        Assert.Throws<DomainException>(() => checklist.Answer(new Dictionary<int, RuleAnswer> { [RuleId(strategy, "Later rule")] = RuleAnswer.Met }, Now));
    }

    [Fact]
    public async Task Adherence_counts_only_completed_trades_with_complete_checklists_and_flags_small_samples()
    {
        var strategy = Orb();
        int Id(string t) => RuleId(strategy, t);
        Trade OnOrb(decimal pnl, decimal rr, TradeStatus status = TradeStatus.Closed)
        {
            var t = TradeFactory.Trade("2026-10-20", "09:30", pnl, rr);
            t.Strategy = strategy;
            t.StrategyId = strategy.Id;
            t.SetStatus(status);
            return t;
        }
        TradeChecklist Answered(Trade t, RuleAnswer stop, RuleAnswer risk, RuleAnswer? news = RuleAnswer.Met)
        {
            var c = new TradeChecklist(t.Id, strategy, Now);
            var answers = new Dictionary<int, RuleAnswer> { [Id("Stop loss is set")] = stop, [Id("Risk within 1%")] = risk };
            if (news is { } n) answers[Id("News checked")] = n;
            c.Answer(answers, Now);
            return c;
        }

        var followed1 = OnOrb(200, 2);
        var followed2 = OnOrb(-100, -1);
        var broke = OnOrb(-300, -3);
        var unfinished = OnOrb(50, 0.5m);
        var none = OnOrb(80, 0.8m);
        var draft = OnOrb(500, 5);
        draft.SetStatus(TradeStatus.Draft);
        var checklists = new List<TradeChecklist>
        {
            Answered(followed1, RuleAnswer.Met, RuleAnswer.Met),
            Answered(followed2, RuleAnswer.Met, RuleAnswer.Met, RuleAnswer.NotMet),
            Answered(broke, RuleAnswer.Met, RuleAnswer.NotMet),
            Answered(unfinished, RuleAnswer.Met, RuleAnswer.Met, news: null),
            Answered(draft, RuleAnswer.NotMet, RuleAnswer.NotMet)
        };

        var service = new AdherenceService(new InMemoryTradeRepository(followed1, followed2, broke, unfinished, none, draft), new ChecklistOnlyRepository(checklists));
        var report = await service.GetReportAsync(1);

        Assert.Equal(5, report.CompletedTrades);   // the draft is not a completed trade
        Assert.Equal(3, report.WithChecklist);
        Assert.Equal(1, report.IncompleteChecklist);
        Assert.Equal(1, report.WithoutChecklist);
        Assert.Equal(2, report.Compliant);
        Assert.Equal(66.67m, report.AdherenceRate);   // 2 ÷ 3
        Assert.Equal(new AdherenceGroupDto(2, 1, 100m, 50m, 0.5m, InsufficientData: true), report.CompliantResults);
        Assert.Equal(-300m, report.NonCompliantResults.NetPnL);
        var violation = Assert.Single(report.MostViolated);
        Assert.Equal(("Risk within 1%", 1, 3), (violation.Text, violation.TimesNotMet, violation.TimesAnswered));
        Assert.Equal(66.67m, Assert.Single(report.ByStrategy).AdherenceRate);
    }

    [Fact]
    public async Task Adherence_has_no_rate_without_a_complete_checklist()
    {
        var service = new AdherenceService(new InMemoryTradeRepository(TradeFactory.Trade("2026-10-20", "09:30", 100, 1)), new ChecklistOnlyRepository([]));
        var report = await service.GetReportAsync(1);

        Assert.Null(report.AdherenceRate);
        Assert.Equal(1, report.WithoutChecklist);
        Assert.Null(report.CompliantResults.WinRate);
    }

    private sealed class ChecklistOnlyRepository(List<TradeChecklist> checklists) : IStrategyRepository
    {
        public Task<List<TradeChecklist>> GetChecklistsAsync(IReadOnlyCollection<int> tradeIds) =>
            Task.FromResult(checklists.Where(c => tradeIds.Contains(c.TradeId)).ToList());

        public Task<Strategy?> GetByNameAsync(int userId, string name) => throw new NotSupportedException();
        public Task<Strategy?> GetForUserAsync(int strategyId, int userId) => throw new NotSupportedException();
        public Task<List<Strategy>> GetAllForUserAsync(int userId) => throw new NotSupportedException();
        public Task<Dictionary<int, int>> CountTradesAsync(int userId) => throw new NotSupportedException();
        public Task<Strategy> GetOrCreateAsync(int userId, string name, DateTime now) => throw new NotSupportedException();
        public Task<TradeChecklist?> GetChecklistAsync(int tradeId, int strategyId) => throw new NotSupportedException();
        public void Add(TradeChecklist checklist) => throw new NotSupportedException();
        public Task<Strategy?> GetByIdAsync(int id) => throw new NotSupportedException();
        public Task<IEnumerable<Strategy>> GetAllAsync() => throw new NotSupportedException();
        public Task AddAsync(Strategy entity) => throw new NotSupportedException();
        public Task UpdateAsync(Strategy entity) => throw new NotSupportedException();
        public Task DeleteAsync(Strategy entity) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync() => throw new NotSupportedException();
    }
}
