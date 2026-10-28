using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Strategies owned per trader, their rules, checklists on trades with history, and the adherence report
public class StrategyEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StrategyEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Strategy(string name, params object[] rules) => new { name, rules };
    private static object Rule(string text, bool mandatory, int? id = null) => new { id, text, isMandatory = mandatory };

    private static object Closed(string instrument, string? strategyName, object? checklist = null, decimal pnL = 50m) => new
    {
        status = "Closed", tradeDate = "2026-10-26", instrument, direction = "Long", entryTime = "09:30:00",
        entryPrice = 100m, exitPrice = 101m, pnL, riskAmount = 25m, session = "London", strategyName, checklist
    };

    private static int RuleId(JsonElement strategy, string text) =>
        strategy.GetProperty("rules").EnumerateArray().Single(r => r.GetProperty("text").GetString() == text).GetProperty("id").GetInt32();

    private static object[] Answers(params (int RuleId, string Answer)[] answers) =>
        answers.Select(a => (object)new { ruleId = a.RuleId, answer = a.Answer }).ToArray();

    [Fact]
    public async Task Strategies_belong_to_one_trader_and_names_are_unique_per_trader()
    {
        var owner = await Owner();
        var other = await Other();

        var created = await Ok(owner.PostAsJsonAsync("/api/strategies", Strategy("Private ORB", Rule("Stop set", true))));
        var id = created.GetProperty("id").GetInt32();

        var duplicate = await owner.PostAsJsonAsync("/api/strategies", Strategy("private orb"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // The other trader can use the same name, and cannot see or change the owner's strategy
        await Ok(other.PostAsJsonAsync("/api/strategies", Strategy("Private ORB")));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/strategies/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/strategies/{id}", Strategy("Taken"))).StatusCode);
        var otherList = await Ok(other.GetAsync("/api/strategies"));
        Assert.DoesNotContain(otherList.EnumerateArray(), s => s.GetProperty("id").GetInt32() == id);

        // A strategy named on a trade is created for that trader only
        await Ok(other.PostAsJsonAsync("/api/trades", Closed("STR-NEW", "Named on a trade")));
        Assert.DoesNotContain((await Ok(owner.GetAsync("/api/strategies"))).EnumerateArray(), s => s.GetProperty("name").GetString() == "Named on a trade");
        Assert.Contains((await Ok(other.GetAsync("/api/strategies"))).EnumerateArray(), s => s.GetProperty("name").GetString() == "Named on a trade");
    }

    [Fact]
    public async Task Concurrent_trades_naming_a_new_strategy_create_it_once()
    {
        var client = await Owner();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => client.PostAsJsonAsync("/api/trades", Closed($"RACE-{i}", "Race strategy"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var list = await Ok(client.GetAsync("/api/strategies"));
        var race = Assert.Single(list.EnumerateArray(), s => s.GetProperty("name").GetString() == "Race strategy");
        Assert.Equal(5, race.GetProperty("tradeCount").GetInt32());
    }

    [Fact]
    public async Task Removed_rules_are_retired_and_can_be_restored_and_invalid_strategies_are_refused()
    {
        var client = await Owner();
        var created = await Ok(client.PostAsJsonAsync("/api/strategies", Strategy("Rules ORB", Rule("Stop set", true), Rule("News checked", false))));
        var id = created.GetProperty("id").GetInt32();
        var stop = RuleId(created, "Stop set");
        var news = RuleId(created, "News checked");

        var edited = await Ok(client.PutAsJsonAsync($"/api/strategies/{id}", Strategy("Rules ORB", Rule("Stop beyond the range", true, stop))));
        Assert.Equal(stop, RuleId(edited, "Stop beyond the range"));
        Assert.Equal(news, Assert.Single(edited.GetProperty("retiredRules").EnumerateArray()).GetProperty("id").GetInt32());

        var restored = await Ok(client.PostAsync($"/api/strategies/{id}/rules/{news}/restore", null));
        Assert.Equal(["Stop beyond the range", "News checked"], restored.GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("text").GetString()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/strategies/{id}", Strategy("Rules ORB", Rule("A", true), Rule("a", false)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/strategies", Strategy(""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/strategies", Strategy("Long", Rule(new string('x', 201), true)))).StatusCode);

        var archived = await Ok(client.PostAsync($"/api/strategies/{id}/archive", null));
        Assert.Equal("Archived", archived.GetProperty("status").GetString());
        Assert.DoesNotContain((await Ok(client.GetAsync("/api/strategies"))).EnumerateArray(), s => s.GetProperty("id").GetInt32() == id);
        Assert.Contains((await Ok(client.GetAsync("/api/strategies?includeArchived=true"))).EnumerateArray(), s => s.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task A_trade_records_its_checklist_and_keeps_every_change_as_history()
    {
        var client = await Owner();
        var strategy = await Ok(client.PostAsJsonAsync("/api/strategies", Strategy("Checklist ORB", Rule("Stop set", true), Rule("Risk within 1%", true), Rule("News checked", false))));
        var sid = strategy.GetProperty("id").GetInt32();
        var stop = RuleId(strategy, "Stop set");
        var risk = RuleId(strategy, "Risk within 1%");
        var news = RuleId(strategy, "News checked");

        var trade = await Ok(client.PostAsJsonAsync("/api/trades",
            Closed("CHK-1", "Checklist ORB", Answers((stop, "Met"), (risk, "Met"), (news, "NotApplicable")))));
        var checklist = trade.GetProperty("checklist");
        Assert.True(checklist.GetProperty("isComplete").GetBoolean());
        Assert.True(checklist.GetProperty("isCompliant").GetBoolean());
        var tradeId = trade.GetProperty("id").GetInt32();

        // Rewording a rule later does not change what this checklist asked
        await Ok(client.PutAsJsonAsync($"/api/strategies/{sid}", Strategy("Checklist ORB",
            Rule("Stop placed beyond the range", true, stop), Rule("Risk within 1%", true, risk), Rule("News checked", false, news))));

        var update = Closed("CHK-1", "Checklist ORB", Answers((risk, "NotMet")));
        var updated = await Ok(client.PutAsJsonAsync($"/api/trades/{tradeId}", update));
        checklist = updated.GetProperty("checklist");
        Assert.False(checklist.GetProperty("isCompliant").GetBoolean());
        Assert.Equal(["Risk within 1%"], checklist.GetProperty("violations").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("Stop set", checklist.GetProperty("items")[0].GetProperty("text").GetString());
        var history = checklist.GetProperty("history").EnumerateArray()
            .Where(h => h.GetProperty("ruleId").GetInt32() == risk)
            .Select(h => (h.GetProperty("sequence").GetInt32(), h.GetProperty("answer").GetString())).ToList();
        Assert.Equal([(1, "Met"), (2, "NotMet")], history);

        // Saving the trade without checklist answers leaves the checklist as it is
        var untouched = await Ok(client.PutAsJsonAsync($"/api/trades/{tradeId}", Closed("CHK-1", "Checklist ORB")));
        Assert.Equal(4, untouched.GetProperty("checklist").GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task Checklist_answers_that_do_not_fit_the_strategy_are_refused_before_the_trade_is_saved()
    {
        var client = await Owner();
        var strategy = await Ok(client.PostAsJsonAsync("/api/strategies", Strategy("Strict ORB", Rule("Stop set", true))));
        var other = await Ok(client.PostAsJsonAsync("/api/strategies", Strategy("Other ORB", Rule("Other rule", true))));
        var stop = RuleId(strategy, "Stop set");

        var notApplicable = await client.PostAsJsonAsync("/api/trades", Closed("BAD-1", "Strict ORB", Answers((stop, "NotApplicable"))));
        var foreign = await client.PostAsJsonAsync("/api/trades", Closed("BAD-2", "Strict ORB", Answers((RuleId(other, "Other rule"), "Met"))));
        var noStrategy = await client.PostAsJsonAsync("/api/trades", Closed("BAD-3", null, Answers((stop, "Met"))));
        var badAnswer = await client.PostAsJsonAsync("/api/trades", Closed("BAD-4", "Strict ORB", Answers((stop, "Maybe"))));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, notApplicable.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noStrategy.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badAnswer.StatusCode);
        var trades = await Ok(client.GetAsync("/api/trades"));
        Assert.DoesNotContain(trades.EnumerateArray(), t => t.GetProperty("instrument").GetString()!.StartsWith("BAD-"));
    }

    [Fact]
    public async Task Adherence_is_compliant_over_complete_checklists_with_results_and_sample_sizes()
    {
        var client = await Owner();
        var strategy = await Ok(client.PostAsJsonAsync("/api/strategies", Strategy("Adherence ORB", Rule("Stop set", true), Rule("News checked", false))));
        var sid = strategy.GetProperty("id").GetInt32();
        var stop = RuleId(strategy, "Stop set");
        var news = RuleId(strategy, "News checked");

        await Ok(client.PostAsJsonAsync("/api/trades", Closed("ADH-1", "Adherence ORB", Answers((stop, "Met"), (news, "NotMet")), pnL: 120m)));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("ADH-2", "Adherence ORB", Answers((stop, "Met"), (news, "Met")), pnL: -40m)));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("ADH-3", "Adherence ORB", Answers((stop, "NotMet"), (news, "Met")), pnL: -90m)));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("ADH-4", "Adherence ORB", Answers((stop, "Met")), pnL: 10m)));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("ADH-5", "Adherence ORB", pnL: 30m)));
        await Ok(client.PostAsJsonAsync("/api/trades", new
        {
            status = "Draft", tradeDate = "2026-10-26", instrument = "ADH-6", session = "London", strategyName = "Adherence ORB",
            checklist = Answers((stop, "NotMet"), (news, "NotMet"))
        }));

        var report = await Ok(client.GetAsync($"/api/analytics/adherence?strategyId={sid}"));

        Assert.Equal(5, report.GetProperty("completedTrades").GetInt32());
        Assert.Equal(3, report.GetProperty("withChecklist").GetInt32());
        Assert.Equal(1, report.GetProperty("incompleteChecklist").GetInt32());
        Assert.Equal(1, report.GetProperty("withoutChecklist").GetInt32());
        Assert.Equal(66.67m, report.GetProperty("adherenceRate").GetDecimal());
        var compliant = report.GetProperty("compliantResults");
        Assert.Equal(2, compliant.GetProperty("trades").GetInt32());
        Assert.Equal(80m, compliant.GetProperty("netPnL").GetDecimal());
        Assert.True(compliant.GetProperty("insufficientData").GetBoolean());
        Assert.Equal(20, report.GetProperty("minimumSample").GetInt32());
        var violation = Assert.Single(report.GetProperty("mostViolated").EnumerateArray());
        Assert.Equal("Stop set", violation.GetProperty("text").GetString());
        Assert.Equal(1, violation.GetProperty("timesNotMet").GetInt32());

        // The other trader's report does not see these trades
        var others = await Ok((await Other()).GetAsync($"/api/analytics/adherence?strategyId={sid}"));
        Assert.Equal(0, others.GetProperty("completedTrades").GetInt32());
    }
}
