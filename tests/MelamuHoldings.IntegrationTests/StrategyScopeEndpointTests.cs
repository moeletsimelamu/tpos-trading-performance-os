using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Rule categories, the instruments and sessions a strategy is for, and its default journal template, through the API
public class StrategyScopeEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StrategyScopeEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<int> Template(HttpClient client, string name) =>
        (await Ok(client.PostAsJsonAsync("/api/journal-templates", new
        {
            name,
            sections = new[] { new { id = "lesson", title = "Lesson", fields = new[] { new { id = "lesson", label = "Lesson", type = "ShortText" } } } }
        }))).GetProperty("id").GetInt32();

    private static object Body(string name, int? defaultTemplateId, params object[] rules) => new
    {
        name, rules, instruments = new[] { " NQ ", "ES", "nq" }, sessions = new[] { "New York" }, defaultTemplateId
    };

    [Fact]
    public async Task Categories_scope_and_default_template_are_saved_checked_and_carried_to_checklists()
    {
        var client = await Owner();
        var scalp = await Template(client, "Scope scalp journal");
        var created = await Ok(client.PostAsJsonAsync("/api/strategies", Body("Scope ORB", scalp,
            new { text = "Range marked", isMandatory = true, category = "Setup" },
            new { text = "Stop beyond the range", isMandatory = true, category = "Risk" },
            new { text = "Trail after 1R", isMandatory = false })));
        var id = created.GetProperty("id").GetInt32();

        Assert.Equal(["Setup", "Risk", "General"], created.GetProperty("rules").EnumerateArray().Select(r => r.GetProperty("category").GetString()));
        Assert.Equal(["NQ", "ES"], created.GetProperty("instruments").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal(["New York"], created.GetProperty("sessions").EnumerateArray().Select(i => i.GetString()));
        Assert.Equal(scalp, created.GetProperty("defaultTemplateId").GetInt32());

        // A trade's checklist carries each rule's category, and so does the most-missed list
        var stop = created.GetProperty("rules")[1].GetProperty("id").GetInt32();
        var range = created.GetProperty("rules")[0].GetProperty("id").GetInt32();
        var trail = created.GetProperty("rules")[2].GetProperty("id").GetInt32();
        var trade = await Ok(client.PostAsJsonAsync("/api/trades", new
        {
            status = "Closed", tradeDate = "2037-01-05", instrument = "GC", direction = "Long", entryTime = "09:30:00", entryPrice = 100m,
            exitPrice = 99m, pnL = -50m, riskAmount = 50m, session = "London", strategyName = "Scope ORB",
            checklist = new[] { new { ruleId = range, answer = "Met" }, new { ruleId = stop, answer = "NotMet" }, new { ruleId = trail, answer = "NotApplicable" } }
        }));
        Assert.Equal(["Setup", "Risk", "General"], trade.GetProperty("checklist").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("category").GetString()));
        var report = await Ok(client.GetAsync($"/api/analytics/adherence?strategyId={id}"));
        Assert.Equal("Risk", report.GetProperty("mostViolated")[0].GetProperty("category").GetString());
        // A trade outside the strategy's instruments and session is saved: the scope informs, it does not refuse
        Assert.Equal("GC", trade.GetProperty("instrument").GetString());

        // The default template must be the trader's own and active; one archived after it was chosen may be kept
        var othersTemplate = await Template(await Other(), "Someone else's journal");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/strategies/{id}", Body("Scope ORB", othersTemplate))).StatusCode);
        var archived = await Template(client, "Scope archived journal");
        await Ok(client.PostAsync($"/api/journal-templates/{archived}/archive", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/strategies/{id}", Body("Scope ORB", archived))).StatusCode);
        await Ok(client.PostAsync($"/api/journal-templates/{scalp}/archive", null));
        var kept = await Ok(client.PutAsJsonAsync($"/api/strategies/{id}", Body("Scope ORB", scalp)));
        Assert.Equal(scalp, kept.GetProperty("defaultTemplateId").GetInt32());
        var cleared = await Ok(client.PutAsJsonAsync($"/api/strategies/{id}", new { name = "Scope ORB", rules = Array.Empty<object>() }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("defaultTemplateId").ValueKind);
        Assert.Empty(cleared.GetProperty("instruments").EnumerateArray());   // anywhere

        // Unknown categories and over-long scope entries are refused
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/strategies/{id}",
            new { name = "Scope ORB", rules = new[] { new { text = "x", isMandatory = true, category = "Psychology" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/strategies/{id}",
            new { name = "Scope ORB", rules = Array.Empty<object>(), instruments = new[] { new string('X', 51) } })).StatusCode);
    }
}
