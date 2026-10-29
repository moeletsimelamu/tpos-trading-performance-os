using System.Text.Json;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.UnitTests;

// Results by journal answer, and rule adherence period by period
public class JournalAnswerAnalyticsTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<JournalSection> Questions =
    [
        new("psych", "Psychology", null,
        [
            new JournalField("emotion", "Emotion before entry", JournalFieldType.SingleSelect, Options: ["Calm", "Anxious", "Bored"]),
            new JournalField("influences", "What influenced the trade", JournalFieldType.MultiSelect, Options: ["FOMO", "Hesitation"]),
            new JournalField("notes", "Reflections", JournalFieldType.LongText)
        ])
    ];

    private static Trade T(int id, string date, decimal pnl, TradeStatus status = TradeStatus.Closed, int? strategyId = null)
    {
        var t = TradeFactory.Trade(date, "09:30", pnl, rr: pnl / 100m);
        t.Id = id;
        t.Status = status;
        t.StrategyId = strategyId;
        return t;
    }

    private static IReadOnlyDictionary<string, JsonElement> Answers(object answers) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(answers))!;

    private static (AnalyticsScope Scope, JournalAnswerAnalytics Answers) Setup()
    {
        var template = new JournalTemplate(1, "Mine", null, Questions, Now);
        var v = template.Current;
        var trades = new[]
        {
            T(1, "2026-10-05", 100), T(2, "2026-10-06", -40), T(3, "2026-10-07", 60), T(4, "2026-10-08", 10),
            T(5, "2026-10-09", 500, TradeStatus.Draft)
        };
        var entries = new[]
        {
            new TradeJournalEntry(1, v, Answers(new { emotion = "Calm", influences = new[] { "FOMO" } }), Now),
            new TradeJournalEntry(2, v, Answers(new { emotion = "Anxious", influences = new[] { "FOMO", "Hesitation" } }), Now),
            new TradeJournalEntry(3, v, Answers(new { emotion = "Calm" }), Now),
            new TradeJournalEntry(5, v, Answers(new { emotion = "Calm" }), Now)   // a draft: never counted
        };
        var journal = new InMemoryJournal(entries, [template]);
        var scope = new AnalyticsScope(new InMemoryTradeRepository(trades), new InMemoryChecklists(), journal);
        return (scope, new JournalAnswerAnalytics(scope, journal));
    }

    [Fact]
    public async Task Each_answer_row_holds_exactly_the_trades_its_filter_keeps()
    {
        var (scope, answers) = Setup();

        var emotion = await answers.ByAnswerAsync(1, "emotion", null);

        Assert.Equal(4, emotion.CompletedTrades);   // the draft is left out
        Assert.Equal(["Calm", "Anxious", "Bored"], emotion.Answers.Select(a => a.Label));
        var calm = emotion.Answers[0].Results;
        Assert.Equal((2, 2, 160m, 100m, 0.8m), (calm.Trades, calm.Wins, calm.NetPnL, calm.WinRate, calm.AverageR));
        Assert.Equal((1, -40m), (emotion.Answers[1].Results.Trades, emotion.Answers[1].Results.NetPnL));
        Assert.Equal(0, emotion.Answers[2].Results.Trades);   // an answer nobody gave is shown as none, not hidden
        Assert.Null(emotion.Answers[2].Results.WinRate);
        Assert.Equal((1, 10m), (emotion.NotAnswered.Results.Trades, emotion.NotAnswered.Results.NetPnL));
        Assert.True(calm.InsufficientData);
        Assert.False(emotion.CountsTradesMoreThanOnce);

        // A row agrees with the same answer used as a filter
        var service = new AnalyticsService(scope);
        foreach (var row in emotion.Answers)
        {
            var filtered = await service.GetSummaryAsync(1, new AnalyticsFilter { AnswerField = "emotion", AnswerValue = row.Value });
            Assert.Equal((row.Results.Trades, row.Results.NetPnL), (filtered.TotalTrades, filtered.TotalPnL));
        }
    }

    [Fact]
    public async Task A_multi_select_counts_a_trade_under_each_answer_and_says_so()
    {
        var (_, answers) = Setup();

        var influences = await answers.ByAnswerAsync(1, "influences", null);

        Assert.True(influences.CountsTradesMoreThanOnce);
        Assert.Equal([2, 1], influences.Answers.Select(a => a.Results.Trades));
        Assert.Equal(2, influences.NotAnswered.Results.Trades);
    }

    [Fact]
    public async Task Only_questions_with_fixed_answers_can_be_grouped()
    {
        var (_, answers) = Setup();

        await Assert.ThrowsAsync<NotFoundException>(() => answers.ByAnswerAsync(1, "notes", null));
        await Assert.ThrowsAsync<NotFoundException>(() => answers.ByAnswerAsync(1, "missing", null));
    }

    [Fact]
    public async Task Adherence_by_month_includes_empty_months_and_scores_complete_checklists_only()
    {
        var strategy = new Strategy(1, "ORB", null, Now);
        strategy.SetRules([new RuleDraft(null, "Stop set", true)], Now);
        typeof(Strategy).GetProperty(nameof(Strategy.Id))!.SetValue(strategy, 9);
        typeof(StrategyRule).GetProperty(nameof(StrategyRule.Id))!.SetValue(strategy.Rules[0], 90);
        TradeChecklist Answered(int tradeId, RuleAnswer? answer)
        {
            var c = new TradeChecklist(tradeId, strategy, Now);
            if (answer is { } a) c.Answer(new Dictionary<int, RuleAnswer> { [90] = a }, Now);
            return c;
        }
        var trades = new[]
        {
            T(1, "2026-10-07", 100, strategyId: 9), T(2, "2026-10-21", -40, strategyId: 9), T(3, "2026-10-22", 5, strategyId: 9),
            T(4, "2026-10-23", 5), T(5, "2026-12-02", 60, strategyId: 9), T(6, "2026-12-03", 60, TradeStatus.Open, strategyId: 9)
        };
        var checklists = new InMemoryChecklists(Answered(1, RuleAnswer.Met), Answered(2, RuleAnswer.NotMet), Answered(3, null), Answered(5, RuleAnswer.Met), Answered(6, RuleAnswer.NotMet));
        var service = new AdherenceService(new AnalyticsScope(new InMemoryTradeRepository(trades), checklists, new InMemoryJournal()), checklists);

        var months = await service.GetTrendAsync(1, TrendPeriod.Month);

        Assert.Equal(["2026-10-01", "2026-11-01", "2026-12-01"], months.Periods.Select(p => p.Start.ToString("yyyy-MM-dd")));
        var october = months.Periods[0];
        Assert.Equal((4, 2, 1, 50m), (october.CompletedTrades, october.Scored, october.Compliant, october.AdherenceRate));   // unfinished and missing checklists are not scored
        Assert.Equal(DateOnly.Parse("2026-10-31"), october.End);
        Assert.Equal((0, 0, (decimal?)null), (months.Periods[1].CompletedTrades, months.Periods[1].Scored, months.Periods[1].AdherenceRate));
        Assert.Equal((1, 1, 100m), (months.Periods[2].CompletedTrades, months.Periods[2].Scored, months.Periods[2].AdherenceRate));   // the open trade is not counted
        Assert.All(months.Periods, p => Assert.True(p.InsufficientData));

        var weeks = await service.GetTrendAsync(1, TrendPeriod.Week);
        Assert.Equal(DateOnly.Parse("2026-10-05"), weeks.Periods[0].Start);   // Wednesday 7 October is in the week from Monday 5 October
        Assert.Equal(DayOfWeek.Monday, weeks.Periods[0].Start.DayOfWeek);
        Assert.Equal(9, weeks.Periods.Count);   // 5 October to the week of 2 December
        Assert.Equal(2, weeks.Periods.Sum(p => p.Compliant));

        Assert.Empty((await new AdherenceService(new AnalyticsScope(new InMemoryTradeRepository(), checklists, new InMemoryJournal()), checklists)
            .GetTrendAsync(1, TrendPeriod.Month)).Periods);
    }
}
