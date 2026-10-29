using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Results by journal answer and adherence period by period, through the API: same trades and filters as every figure
public class JournalAnswerEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public JournalAnswerEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Results_by_answer_and_adherence_by_month_follow_the_filter_and_stay_private()
    {
        var client = await Owner();
        var strategy = await Ok(client.PostAsJsonAsync("/api/strategies", new { name = "Breakdown ORB", rules = new[] { new { text = "Stop set", isMandatory = true } } }));
        var rule = strategy.GetProperty("rules")[0].GetProperty("id").GetInt32();
        var template = await Ok(client.PostAsJsonAsync("/api/journal-templates", new
        {
            name = "Breakdown template",
            sections = new[] { new { id = "mind", title = "Mind", fields = new[] { new { id = "mood-bd", label = "Mood", type = "SingleSelect", options = new[] { "Calm", "Rushed" } } } } }
        }));
        var version = template.GetProperty("currentVersionId").GetInt32();
        Task<JsonElement> Trade(string date, decimal pnL, string mood, string answer) => Ok(client.PostAsJsonAsync("/api/trades", new
        {
            status = "Closed", tradeDate = date, instrument = "BD-NQ", direction = "Long", entryTime = "09:30:00", entryPrice = 100m,
            exitPrice = 101m, pnL, riskAmount = 50m, session = "London", strategyName = "Breakdown ORB",
            journal = new { templateVersionId = version, answers = new Dictionary<string, string> { ["mood-bd"] = mood } },
            checklist = new[] { new { ruleId = rule, answer } }
        }));
        await Trade("2036-01-10", 100m, "Calm", "Met");
        await Trade("2036-01-20", -50m, "Rushed", "NotMet");
        await Trade("2036-03-05", 40m, "Calm", "Met");
        await Ok(client.PostAsJsonAsync("/api/trades", new { status = "Closed", tradeDate = "2036-03-06", instrument = "BD-ES", direction = "Long", entryTime = "09:30:00", entryPrice = 1m, exitPrice = 2m, pnL = 999m, session = "London" }));

        var mood = await Ok(client.GetAsync("/api/analytics/by-answer?field=mood-bd&instrument=BD-NQ"));
        Assert.Equal("Mood", mood.GetProperty("label").GetString());
        Assert.Equal(3, mood.GetProperty("completedTrades").GetInt32());
        var rows = mood.GetProperty("answers").EnumerateArray().ToDictionary(a => a.GetProperty("value").GetString()!, a => a.GetProperty("results"));
        Assert.Equal((2, 140m), (rows["Calm"].GetProperty("trades").GetInt32(), rows["Calm"].GetProperty("netPnL").GetDecimal()));
        Assert.Equal((1, -50m), (rows["Rushed"].GetProperty("trades").GetInt32(), rows["Rushed"].GetProperty("netPnL").GetDecimal()));
        Assert.Equal(0, mood.GetProperty("notAnswered").GetProperty("results").GetProperty("trades").GetInt32());

        // The filter narrows the breakdown like every other figure
        var january = await Ok(client.GetAsync("/api/analytics/by-answer?field=mood-bd&instrument=BD-NQ&to=2036-01-31"));
        Assert.Equal(2, january.GetProperty("completedTrades").GetInt32());

        var trend = await Ok(client.GetAsync("/api/analytics/adherence-trend?instrument=BD-NQ"));
        Assert.Equal("Month", trend.GetProperty("period").GetString());
        var months = trend.GetProperty("periods").EnumerateArray().ToList();
        Assert.Equal(["2036-01-01", "2036-02-01", "2036-03-01"], months.Select(m => m.GetProperty("start").GetString()));
        Assert.Equal(50m, months[0].GetProperty("adherenceRate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, months[1].GetProperty("adherenceRate").ValueKind);   // no trades in February: no rate, not 0%
        Assert.Equal(100m, months[2].GetProperty("adherenceRate").GetDecimal());
        Assert.Equal("Week", (await Ok(client.GetAsync("/api/analytics/adherence-trend?instrument=BD-NQ&period=week"))).GetProperty("period").GetString());

        // Questions belong to their trader; bad requests are refused
        Assert.Equal(HttpStatusCode.NotFound, (await (await Other()).GetAsync("/api/analytics/by-answer?field=mood-bd")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/analytics/by-answer")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/analytics/adherence-trend?period=year")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/analytics/by-answer?field=mood-bd&from=2036-02-01&to=2036-01-01")).StatusCode);
    }
}
