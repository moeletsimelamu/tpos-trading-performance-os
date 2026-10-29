using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Change history: every save that changes a trade, its journal answers, its checklist or its files is recorded
// with what each value was before and after. Nothing is overwritten without a record.
public class TradeHistoryEndpointTests : IClassFixture<ApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1];

    private readonly ApiFactory _factory;

    public TradeHistoryEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<JsonElement> History(JsonElement history) => history.EnumerateArray().ToList();

    private static Dictionary<string, (string? Before, string? After)> Changes(JsonElement revision) =>
        revision.GetProperty("changes").EnumerateArray().ToDictionary(
            c => c.GetProperty("label").GetString()!,
            c => (c.GetProperty("before").GetString(), c.GetProperty("after").GetString()));

    private static object Trade(string status, int versionId, object answers, object[] checklist, string notes, bool closed) => new
    {
        status, tradeDate = "2031-09-01", instrument = "HIST-NQ", direction = "Long", entryTime = "09:30:00", entryPrice = 100m,
        riskAmount = 25m, session = "New York", strategyName = "History ORB", notes,
        exitTime = closed ? "10:15:00" : null, exitPrice = closed ? 101m : (decimal?)null, pnL = closed ? 50m : (decimal?)null,
        journal = new { templateVersionId = versionId, answers },
        checklist
    };

    [Fact]
    public async Task An_edit_records_each_changed_field_answer_and_rule_and_an_unchanged_save_records_nothing()
    {
        var client = await Owner();
        var strategy = await Ok(client.PostAsJsonAsync("/api/strategies", new
        {
            name = "History ORB",
            rules = new[] { new { text = "Stop set before entry", isMandatory = true } }
        }));
        var ruleId = strategy.GetProperty("rules")[0].GetProperty("id").GetInt32();
        var template = await Ok(client.PostAsJsonAsync("/api/journal-templates", new
        {
            name = "History template",
            sections = new[] { new { id = "review", title = "Review", fields = new object[]
            {
                new { id = "lesson", label = "Key lesson", type = "ShortText" },
                new { id = "focus", label = "Focus", type = "Rating" }
            } } }
        }));
        var versionId = template.GetProperty("currentVersionId").GetInt32();

        var open = Trade("Open", versionId, new { lesson = "Wait" }, [new { ruleId, answer = "Met" }], "First notes", closed: false);
        var id = (await Ok(client.PostAsJsonAsync("/api/trades", open))).GetProperty("id").GetInt32();

        var created = Assert.Single(History(await Ok(client.GetAsync($"/api/trades/{id}/history"))));
        Assert.Equal("Created", created.GetProperty("kind").GetString());
        Assert.Equal("Open", created.GetProperty("status").GetString());

        // Close it, correct the notes, change an answer, add one, and change the checklist answer
        var close = Trade("Closed", versionId, new { lesson = "Wait for the retest", focus = 4 }, [new { ruleId, answer = "NotMet" }], "Corrected notes", closed: true);
        await Ok(client.PutAsJsonAsync($"/api/trades/{id}", close));

        var history = History(await Ok(client.GetAsync($"/api/trades/{id}/history")));
        Assert.Equal(2, history.Count);
        var edit = history[0];   // newest first
        Assert.Equal("Edited", edit.GetProperty("kind").GetString());
        Assert.Equal("Closed", edit.GetProperty("status").GetString());
        var changes = Changes(edit);
        Assert.Equal(("Open", "Closed"), changes["Status"]);
        Assert.Equal((null, "101"), changes["Exit price"]);
        Assert.Equal((null, "50"), changes["P&L"]);
        Assert.Equal((null, "2"), changes["R"]);                       // 50 ÷ 25, calculated
        Assert.Equal((null, "45"), changes["Duration (minutes)"]);
        Assert.Equal(("First notes", "Corrected notes"), changes["Notes"]);
        Assert.Equal(("Wait", "Wait for the retest"), changes["Key lesson"]);
        Assert.Equal((null, "4"), changes["Focus"]);
        Assert.Equal(("Met", "Not met"), changes["Rule: Stop set before entry"]);
        // Values that did not change are not listed, whatever precision the database keeps them at
        Assert.DoesNotContain("Entry price", changes.Keys);
        Assert.DoesNotContain("Risk", changes.Keys);
        Assert.DoesNotContain("Journal template", changes.Keys);

        // Saving the same values again changes nothing, so nothing is recorded
        await Ok(client.PutAsJsonAsync($"/api/trades/{id}", close));
        Assert.Equal(2, History(await Ok(client.GetAsync($"/api/trades/{id}/history"))).Count);

        // Marking it reviewed keeps the review text, and the trade's own record still shows the latest values
        await Ok(client.PostAsJsonAsync($"/api/trades/{id}/review", new { reviewNotes = "Followed the plan apart from the stop." }));
        var reviewed = History(await Ok(client.GetAsync($"/api/trades/{id}/history")))[0];
        Assert.Equal("Reviewed", reviewed.GetProperty("kind").GetString());
        Assert.Equal((null, "Followed the plan apart from the stop."), Changes(reviewed)["Review"]);
        Assert.Equal(("Closed", "Reviewed"), Changes(reviewed)["Status"]);

        // Correcting the review afterwards keeps the earlier wording in the history
        await Ok(client.PostAsJsonAsync($"/api/trades/{id}/review", new { reviewNotes = "Followed the plan; the stop was late." }));
        var corrected = History(await Ok(client.GetAsync($"/api/trades/{id}/history")))[0];
        Assert.Equal(("Followed the plan apart from the stop.", "Followed the plan; the stop was late."), Changes(corrected)["Review"]);
        Assert.DoesNotContain("Status", Changes(corrected).Keys);
    }

    [Fact]
    public async Task Files_added_and_removed_are_recorded_and_the_history_is_private_and_goes_with_the_trade()
    {
        var client = await Owner();
        var id = (await Ok(client.PostAsJsonAsync("/api/trades", new { status = "Draft", tradeDate = "2031-09-02", instrument = "HIST-FILE" })))
            .GetProperty("id").GetInt32();

        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var attachment = await Ok(client.PostAsync($"/api/trades/{id}/attachments", new MultipartFormDataContent { { file, "file", "entry.png" } }));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/trades/{id}/attachments/{attachment.GetProperty("id").GetInt32()}")).StatusCode);

        var history = History(await Ok(client.GetAsync($"/api/trades/{id}/history")));
        Assert.Equal(["FileRemoved", "FileAdded", "Created"], history.Select(r => r.GetProperty("kind").GetString()!).ToArray());
        Assert.Equal((null, "entry.png"), Changes(history[1])["File"]);
        Assert.Equal(("entry.png", null), Changes(history[0])["File"]);

        var other = await Other();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/trades/{id}/history")).StatusCode);

        // Deleting the trade deletes its history with it
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/trades/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/trades/{id}/history")).StatusCode);
    }
}
