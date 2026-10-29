using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Journal J6: every analytics endpoint takes the same filter; filters narrow the trades, never the calculation
public class AnalyticsFilterEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AnalyticsFilterEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Closed(string date, decimal pnL, string session = "London", string? account = null, string? strategy = null,
        object? checklist = null, object? journal = null, string time = "09:30:00") => new
    {
        status = "Closed", tradeDate = date, instrument = "FLT", direction = "Long", entryTime = time,
        entryPrice = 100m, exitPrice = 101m, pnL, riskAmount = 50m, session, tradingAccount = account,
        strategyName = strategy, checklist, journal
    };

    private static (int Trades, decimal PnL) Totals(JsonElement summary) =>
        (summary.GetProperty("totalTrades").GetInt32(), summary.GetProperty("totalPnL").GetDecimal());

    [Fact]
    public async Task Date_session_and_account_filters_narrow_every_figure_alike()
    {
        var client = await Other();
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-01-05", 200m, account: "Prop A")));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-01-06", -80m, session: "New York", account: "Prop A")));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-01-07", 130m, session: "New York")));
        await Ok(client.PostAsJsonAsync("/api/trades", new { status = "Draft", tradeDate = "2032-01-07", instrument = "FLT", session = "New York", pnL = 999m }));

        const string january = "from=2032-01-01&to=2032-01-31";
        Assert.Equal((3, 250m), Totals(await Ok(client.GetAsync($"/api/analytics/summary?{january}"))));
        Assert.Equal((2, 50m), Totals(await Ok(client.GetAsync($"/api/analytics/summary?{january}&session=new%20york"))));
        Assert.Equal((2, 120m), Totals(await Ok(client.GetAsync($"/api/analytics/summary?{january}&account=Prop%20A"))));

        // The same filter narrows the equity curve, the breakdowns and behaviour
        var curve = (await Ok(client.GetAsync($"/api/analytics/equity-curve?{january}&session=New%20York"))).EnumerateArray().ToList();
        Assert.Equal(["2032-01-06", "2032-01-07"], curve.Select(p => p.GetProperty("date").GetString()));
        Assert.Equal(50m, curve[^1].GetProperty("cumulativePnL").GetDecimal());
        var sessions = (await Ok(client.GetAsync($"/api/analytics/by-session?{january}&account=Prop%20A"))).EnumerateArray()
            .ToDictionary(r => r.GetProperty("label").GetString()!, r => r.GetProperty("totalPnL").GetDecimal());
        Assert.Equal(new Dictionary<string, decimal> { ["London"] = 200m, ["New York"] = -80m }, sessions);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/analytics/behaviour?{january}&session=London")).StatusCode);

        var summary = await Ok(client.GetAsync($"/api/analytics/summary?{january}"));
        Assert.True(summary.GetProperty("insufficientData").GetBoolean());
        Assert.Equal(20, summary.GetProperty("minimumSample").GetInt32());
    }

    [Fact]
    public async Task A_strategy_filter_gives_the_same_figures_as_that_strategy_s_breakdown_row()
    {
        var client = await Other();
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-02-02", 300m, strategy: "Filter ORB")));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-02-03", -120m, strategy: "Filter ORB")));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-02-03", 75m, strategy: "Filter Other")));
        var strategyId = (await Ok(client.GetAsync("/api/strategies"))).EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == "Filter ORB").GetProperty("id").GetInt32();

        var row = (await Ok(client.GetAsync("/api/analytics/by-strategy"))).EnumerateArray()
            .Single(r => r.GetProperty("strategyName").GetString() == "Filter ORB");
        var filtered = await Ok(client.GetAsync($"/api/analytics/summary?strategyId={strategyId}"));

        Assert.Equal((row.GetProperty("totalTrades").GetInt32(), row.GetProperty("totalPnL").GetDecimal()), Totals(filtered));
        Assert.Equal(row.GetProperty("winRate").GetDecimal(), filtered.GetProperty("winRate").GetDecimal());
        Assert.Equal((2, 180m), Totals(filtered));
    }

    [Fact]
    public async Task Adherence_and_journal_answers_filter_the_trades()
    {
        var client = await Owner();
        var strategy = await Ok(client.PostAsJsonAsync("/api/strategies", new { name = "Filter rules", rules = new[] { new { text = "Stop set", isMandatory = true } } }));
        var stop = strategy.GetProperty("rules")[0].GetProperty("id").GetInt32();
        var template = await Ok(client.PostAsJsonAsync("/api/journal-templates", new
        {
            name = "Filter questions",
            sections = new[] { new { id = "psych", title = "Psychology", fields = new object[]
            {
                new { id = "mood", label = "Mood", type = "SingleSelect", options = new[] { "Calm", "Tense" } },
                new { id = "quality", label = "Quality", type = "Rating" }
            } } }
        }));
        var versionId = template.GetProperty("currentVersionId").GetInt32();
        object Journal(string mood, int quality) => new { templateVersionId = versionId, answers = new { mood, quality } };
        object Check(string answer) => new[] { new { ruleId = stop, answer } };

        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-03-02", 100m, strategy: "Filter rules", checklist: Check("Met"), journal: Journal("Calm", 4))));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-03-03", -60m, strategy: "Filter rules", checklist: Check("NotMet"), journal: Journal("Tense", 2))));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-03-04", 40m, strategy: "Filter rules", journal: Journal("Calm", 2))));

        const string march = "from=2032-03-01&to=2032-03-31";
        Assert.Equal((1, 100m), Totals(await Ok(client.GetAsync($"/api/analytics/summary?{march}&adherence=Followed"))));
        Assert.Equal((1, -60m), Totals(await Ok(client.GetAsync($"/api/analytics/summary?{march}&adherence=Broke"))));
        Assert.Equal((1, 40m), Totals(await Ok(client.GetAsync($"/api/analytics/summary?{march}&adherence=NotScored"))));
        Assert.Equal((2, 140m), Totals(await Ok(client.GetAsync($"/api/analytics/summary?{march}&answerField=mood&answerValue=Calm"))));
        Assert.Equal((2, -20m), Totals(await Ok(client.GetAsync($"/api/analytics/summary?{march}&answerField=quality&answerValue=2"))));

        // Adherence takes the same filter: the calm trades include one followed and one unscored
        var report = await Ok(client.GetAsync($"/api/analytics/adherence?{march}&answerField=mood&answerValue=Calm"));
        Assert.Equal((2, 1, 1), (report.GetProperty("completedTrades").GetInt32(), report.GetProperty("withChecklist").GetInt32(), report.GetProperty("withoutChecklist").GetInt32()));

        var options = await Ok(client.GetAsync("/api/analytics/filter-options"));
        var mood = options.GetProperty("journalFields").EnumerateArray().Single(f => f.GetProperty("id").GetString() == "mood");
        Assert.Equal(["Calm", "Tense"], mood.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("value").GetString()));
        Assert.Contains(options.GetProperty("strategies").EnumerateArray(), s => s.GetProperty("label").GetString() == "Filter rules");
    }

    [Fact]
    public async Task Invalid_filters_are_refused_and_options_are_private()
    {
        var client = await Owner();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/analytics/summary?from=2032-05-10&to=2032-05-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/analytics/summary?answerField=mood")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/analytics/by-day?adherence=Sometimes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/analytics/filter-options")).StatusCode);

        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2032-06-01", 10m, account: "Owner only account")));
        var others = await Ok((await Other()).GetAsync("/api/analytics/filter-options"));
        Assert.DoesNotContain("Owner only account", others.GetProperty("accounts").EnumerateArray().Select(a => a.GetString()));
    }
}
