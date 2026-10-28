using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.UnitTests;

public class AnalyticsFilterTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<JournalSection> Questions =
    [
        new("psych", "Psychology", null,
        [
            new JournalField("emotion", "Emotion before entry", JournalFieldType.SingleSelect, Options: ["Calm", "Anxious"]),
            new JournalField("confidence", "Confidence", JournalFieldType.Rating),
            new JournalField("planned-risk", "I followed the planned risk", JournalFieldType.Checkbox),
            new JournalField("influences", "What influenced the trade", JournalFieldType.MultiSelect, Options: ["FOMO", "Hesitation"]),
            new JournalField("notes", "Reflections", JournalFieldType.LongText)
        ])
    ];

    private static Trade T(int id, string date, decimal pnl, string session = "London", string? account = null, int? strategyId = null)
    {
        var t = TradeFactory.Trade(date, "09:30", pnl, rr: pnl / 100m, session: session);
        t.Id = id;
        t.TradingAccount = account;
        t.StrategyId = strategyId;
        return t;
    }

    private static Dictionary<string, JsonElement> Answers(object values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static readonly Trade[] Trades =
    [
        T(1, "2026-10-05", 200, account: "Prop A"),
        T(2, "2026-10-06", -100, session: "New York", account: "Prop A"),
        T(3, "2026-10-07", 300, session: "New York"),
        T(4, "2026-10-12", -50, account: "Prop B"),
        T(5, "2026-10-20", 150)
    ];

    [Fact]
    public async Task Filters_only_narrow_the_trades_and_the_calculation_is_unchanged()
    {
        var filters = new[]
        {
            new AnalyticsFilter { Session = "new york" },
            new AnalyticsFilter { Account = "Prop A" },
            new AnalyticsFilter { From = DateOnly.Parse("2026-10-06"), To = DateOnly.Parse("2026-10-12") },
            new AnalyticsFilter { Session = "London", Account = "Prop B" }
        };
        foreach (var filter in filters)
        {
            AnalyticsScope scope = new InMemoryTradeRepository(Trades);
            var kept = await scope.TradesAsync(1, filter);
            Assert.All(kept, t => Assert.Contains(t, Trades));

            // The filtered summary is exactly the summary of a journal holding only those trades
            var filtered = await new AnalyticsService(new InMemoryTradeRepository(Trades)).GetSummaryAsync(1, filter);
            var direct = await new AnalyticsService(new InMemoryTradeRepository([.. kept])).GetSummaryAsync(1);
            Assert.Equivalent(direct, filtered);
        }
    }

    [Fact]
    public async Task Each_filter_keeps_only_matching_trades()
    {
        AnalyticsScope scope = new InMemoryTradeRepository(Trades);
        async Task<int[]> Ids(AnalyticsFilter f) => (await scope.TradesAsync(1, f)).Select(t => t.Id).Order().ToArray();

        Assert.Equal(new[] { 2, 3 }, await Ids(new AnalyticsFilter { Session = " New York " }));
        Assert.Equal(new[] { 1, 2 }, await Ids(new AnalyticsFilter { Account = "prop a" }));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await Ids(new AnalyticsFilter { Instrument = "nq" }));   // every fixture trade is NQ
        Assert.Empty(await Ids(new AnalyticsFilter { Instrument = "ES" }));
        Assert.Equal(new[] { 2, 3, 4 }, await Ids(new AnalyticsFilter { From = DateOnly.Parse("2026-10-06"), To = DateOnly.Parse("2026-10-12") }));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, await Ids(new AnalyticsFilter()));
    }

    [Fact]
    public async Task Adherence_filter_uses_the_checklist_of_the_trade_s_current_strategy()
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
        var trades = new[] { T(1, "2026-10-05", 100, strategyId: 9), T(2, "2026-10-05", -40, strategyId: 9), T(3, "2026-10-05", 60, strategyId: 9), T(4, "2026-10-05", 10, strategyId: 9) };
        var scope = new AnalyticsScope(new InMemoryTradeRepository(trades),
            new InMemoryChecklists(Answered(1, RuleAnswer.Met), Answered(2, RuleAnswer.NotMet), Answered(3, null)), new InMemoryJournal());
        async Task<int[]> Ids(AdherenceFilter a) => (await scope.TradesAsync(1, new AnalyticsFilter { Adherence = a })).Select(t => t.Id).Order().ToArray();

        Assert.Equal(new[] { 1 }, await Ids(AdherenceFilter.Followed));
        Assert.Equal(new[] { 2 }, await Ids(AdherenceFilter.Broke));
        Assert.Equal(new[] { 3, 4 }, await Ids(AdherenceFilter.NotScored));   // unfinished and missing checklists
    }

    [Fact]
    public async Task Answer_filter_matches_the_question_by_its_id_in_any_template_version()
    {
        var template = new JournalTemplate(1, "Mine", null, Questions, Now);
        var version = template.Current;
        var entries = new[]
        {
            new TradeJournalEntry(1, version, Answers(new { emotion = "Calm", confidence = 4, influences = new[] { "FOMO" } }), Now),
            new TradeJournalEntry(2, version, Answers(new { emotion = "Anxious", confidence = 2, influences = new[] { "FOMO", "Hesitation" } }), Now),
            new TradeJournalEntry(3, version, Answers(new { emotion = "Calm" }), Now)
        };
        var scope = new AnalyticsScope(new InMemoryTradeRepository(Trades), new InMemoryChecklists(), new InMemoryJournal(entries));
        async Task<int[]> Ids(string field, string value) =>
            (await scope.TradesAsync(1, new AnalyticsFilter { AnswerField = field, AnswerValue = value })).Select(t => t.Id).Order().ToArray();

        Assert.Equal(new[] { 1, 3 }, await Ids("emotion", "Calm"));
        Assert.Equal(new[] { 1 }, await Ids("confidence", "4"));
        Assert.Equal(new[] { 1, 2 }, await Ids("influences", "FOMO"));
        Assert.Empty(await Ids("emotion", "Tired"));   // trades without the answer are left out, never guessed
    }

    [Theory]
    [InlineData("\"Calm\"", "calm", true)]
    [InlineData("\"Calm\"", "Anxious", false)]
    [InlineData("4", "4", true)]
    [InlineData("4", "4.0", true)]
    [InlineData("4", "x", false)]
    [InlineData("true", "true", true)]
    [InlineData("false", "true", false)]
    [InlineData("[\"FOMO\",\"Hesitation\"]", "Hesitation", true)]
    [InlineData("{\"Stop set\":true,\"News\":false}", "Stop set", true)]
    [InlineData("{\"Stop set\":true,\"News\":false}", "News", false)]
    public void Answers_match_by_their_shape(string json, string wanted, bool matches) =>
        Assert.Equal(matches, AnalyticsScope.AnswerMatches(JsonDocument.Parse(json).RootElement, wanted));

    [Fact]
    public async Task Options_offer_questions_with_fixed_answers_from_every_version()
    {
        var template = new JournalTemplate(1, "Mine", null, Questions, Now);
        var v2 = Questions.Select(s => s with { Fields = s.Fields.Select(f => f.Id == "emotion" ? f with { Label = "Emotion at entry", Options = ["Calm", "Focused"] } : f).ToList() }).ToList();
        template.Update("Mine", null, v2, Now.AddDays(1));
        typeof(JournalTemplateVersion).GetProperty(nameof(JournalTemplateVersion.Id))!.SetValue(template.Versions[1], 2);
        var scope = new AnalyticsScope(new InMemoryTradeRepository(Trades), new InMemoryChecklists(), new InMemoryJournal(templates: [template]));

        var options = await scope.OptionsAsync(1);

        Assert.Equal(["Confidence", "Emotion at entry", "I followed the planned risk", "What influenced the trade"], options.JournalFields.Select(f => f.Label));
        Assert.Equal(["Calm", "Anxious", "Focused"], options.JournalFields.Single(f => f.Id == "emotion").Options.Select(o => o.Value));
        Assert.Equal(["1", "2", "3", "4", "5"], options.JournalFields.Single(f => f.Id == "confidence").Options.Select(o => o.Value));
        Assert.Equal([("true", "Yes"), ("false", "No")], options.JournalFields.Single(f => f.Id == "planned-risk").Options.Select(o => (o.Value, o.Label)));
        Assert.Equal(["London", "New York"], options.Sessions);
        Assert.Equal(["NQ"], options.Instruments);
        Assert.Equal(["Prop A", "Prop B"], options.Accounts);
    }

    [Fact]
    public void Filters_are_validated()
    {
        static List<ValidationResult> Check(AnalyticsFilter f) => f.Validate(new ValidationContext(f)).ToList();

        Assert.Single(Check(new AnalyticsFilter { From = DateOnly.Parse("2026-10-10"), To = DateOnly.Parse("2026-10-01") }));
        Assert.Single(Check(new AnalyticsFilter { AnswerField = "emotion" }));
        Assert.Empty(Check(new AnalyticsFilter { AnswerField = "emotion", AnswerValue = "Calm" }));
        Assert.True(new AnalyticsFilter { Session = " " }.IsEmpty);
    }

    [Fact]
    public async Task Small_samples_are_flagged_on_the_summary()
    {
        var few = await new AnalyticsService(new InMemoryTradeRepository(Trades)).GetSummaryAsync(1);
        var many = await new AnalyticsService(new InMemoryTradeRepository(
            [.. Enumerable.Range(1, 20).Select(i => T(100 + i, "2026-10-05", i % 2 == 0 ? 10 : -5))])).GetSummaryAsync(1);

        Assert.True(few.InsufficientData);
        Assert.False(many.InsufficientData);
        Assert.Equal(20, many.MinimumSample);
    }
}
