using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Journal templates: default seeding, versioning that leaves old entries intact, answers on trades, review rules
public class JournalTemplateEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public JournalTemplateEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Template(string name, params object[] fields) => new
    {
        name,
        sections = new[] { new { id = "review", title = "Review", fields } }
    };

    private static object Closed(string instrument, int versionId, object answers) => new
    {
        status = "Closed", tradeDate = "2026-10-26", instrument, direction = "Long", entryTime = "09:30:00",
        entryPrice = 100m, exitPrice = 101m, pnL = 50m, riskAmount = 25m, session = "London",
        journal = new { templateVersionId = versionId, answers }
    };

    [Fact]
    public async Task Every_trader_gets_the_standard_template_once_as_their_default()
    {
        var client = await Owner();

        var lists = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Ok(client.GetAsync("/api/journal-templates"))));

        var templates = (await Ok(client.GetAsync("/api/journal-templates"))).EnumerateArray().ToList();
        var standard = Assert.Single(templates, t => t.GetProperty("name").GetString() == "Standard trade journal");
        Assert.True(standard.GetProperty("isDefault").GetBoolean());
        Assert.Equal(5, standard.GetProperty("sections").GetArrayLength());
        Assert.Single(templates, t => t.GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task Editing_a_template_adds_a_version_and_old_entries_keep_theirs()
    {
        var client = await Owner();
        var template = await Ok(client.PostAsJsonAsync("/api/journal-templates",
            Template("Scalping v-test", new { id = "lesson", label = "Lesson", type = "ShortText" })));
        var id = template.GetProperty("id").GetInt32();
        var v1 = template.GetProperty("currentVersionId").GetInt32();

        var oldTrade = await Ok(client.PostAsJsonAsync("/api/trades", Closed("TPL-OLD", v1, new { lesson = "Patience paid" })));

        var edited = await Ok(client.PutAsJsonAsync($"/api/journal-templates/{id}",
            Template("Scalping v-test", new { id = "mood", label = "Mood", type = "Rating" })));
        Assert.Equal(2, edited.GetProperty("currentVersion").GetInt32());
        var versions = edited.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(1, versions.Single(v => v.GetProperty("version").GetInt32() == 1).GetProperty("entryCount").GetInt32());

        // The old trade still reads with v1's question and its own answer
        var reread = await Ok(client.GetAsync($"/api/trades/{oldTrade.GetProperty("id").GetInt32()}"));
        var journal = reread.GetProperty("journal");
        Assert.Equal(1, journal.GetProperty("version").GetInt32());
        Assert.Equal(2, journal.GetProperty("latestVersion").GetInt32());
        Assert.Equal("Lesson", journal.GetProperty("sections")[0].GetProperty("fields")[0].GetProperty("label").GetString());
        Assert.Equal("Patience paid", journal.GetProperty("answers").GetProperty("lesson").GetString());

        // v2 answers are checked against v2
        var v2 = edited.GetProperty("currentVersionId").GetInt32();
        var wrong = await client.PostAsJsonAsync("/api/trades", Closed("TPL-NEW", v2, new { lesson = "No longer a question" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        var right = await Ok(client.PostAsJsonAsync("/api/trades", Closed("TPL-NEW", v2, new { mood = 4 })));
        Assert.Equal(4, right.GetProperty("journal").GetProperty("answers").GetProperty("mood").GetInt32());

        // The past version can still be fetched on its own
        var v1Sections = await Ok(client.GetAsync($"/api/journal-templates/{id}/versions/1"));
        Assert.Equal("lesson", v1Sections[0].GetProperty("fields")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Required_questions_must_be_answered_before_review()
    {
        var client = await Owner();
        var template = await Ok(client.PostAsJsonAsync("/api/journal-templates",
            Template("Required test", new { id = "lesson", label = "Key lesson", type = "ShortText", required = true }, new { id = "notes", label = "Notes", type = "LongText" })));
        var version = template.GetProperty("currentVersionId").GetInt32();
        var trade = await Ok(client.PostAsJsonAsync("/api/trades", Closed("TPL-REQ", version, new { notes = "Some notes" })));
        var tradeId = trade.GetProperty("id").GetInt32();

        var refused = await client.PostAsJsonAsync($"/api/trades/{tradeId}/review", new { reviewNotes = "Done" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Contains("Key lesson", await refused.Content.ReadAsStringAsync());

        await Ok(client.PutAsJsonAsync($"/api/trades/{tradeId}", Closed("TPL-REQ", version, new { notes = "Some notes", lesson = "Wait for the retest" })));
        var reviewed = await Ok(client.PostAsJsonAsync($"/api/trades/{tradeId}/review", new { reviewNotes = "Done" }));
        Assert.Equal("Reviewed", reviewed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Duplicate_archive_restore_and_default_follow_the_rules()
    {
        var client = await Owner();
        var all = (await Ok(client.GetAsync("/api/journal-templates"))).EnumerateArray().ToList();
        var standard = all.Single(t => t.GetProperty("isDefault").GetBoolean());
        var standardId = standard.GetProperty("id").GetInt32();

        var archiveDefault = await client.PostAsync($"/api/journal-templates/{standardId}/archive", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, archiveDefault.StatusCode);

        var copy = await Ok(client.PostAsJsonAsync($"/api/journal-templates/{standardId}/duplicate", new { name = "Swing journal" }));
        var copyId = copy.GetProperty("id").GetInt32();
        Assert.False(copy.GetProperty("isDefault").GetBoolean());
        Assert.Equal(1, copy.GetProperty("currentVersion").GetInt32());

        await Ok(client.PostAsync($"/api/journal-templates/{copyId}/make-default", null));
        var afterSwitch = (await Ok(client.GetAsync("/api/journal-templates"))).EnumerateArray().ToList();
        Assert.Equal(copyId, Assert.Single(afterSwitch, t => t.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetInt32());

        var archived = await Ok(client.PostAsync($"/api/journal-templates/{standardId}/archive", null));
        Assert.Equal("Archived", archived.GetProperty("status").GetString());
        Assert.DoesNotContain((await Ok(client.GetAsync("/api/journal-templates"))).EnumerateArray(), t => t.GetProperty("id").GetInt32() == standardId);
        Assert.Contains((await Ok(client.GetAsync("/api/journal-templates?includeArchived=true"))).EnumerateArray(), t => t.GetProperty("id").GetInt32() == standardId);

        await Ok(client.PostAsync($"/api/journal-templates/{standardId}/restore", null));
        await Ok(client.PostAsync($"/api/journal-templates/{standardId}/make-default", null));
    }

    [Fact]
    public async Task Invalid_templates_are_refused()
    {
        var client = await Owner();

        var duplicateIds = await client.PostAsJsonAsync("/api/journal-templates",
            Template("Bad", new { id = "x", label = "A", type = "ShortText" }, new { id = "x", label = "B", type = "ShortText" }));
        var selectWithoutOptions = await client.PostAsJsonAsync("/api/journal-templates", Template("Bad", new { id = "pick", label = "Pick", type = "SingleSelect" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicateIds.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, selectWithoutOptions.StatusCode);
    }

    [Fact]
    public async Task Templates_and_their_versions_are_private_to_their_owner()
    {
        var owner = await Owner();
        var template = await Ok(owner.PostAsJsonAsync("/api/journal-templates", Template("Private", new { id = "lesson", label = "Lesson", type = "ShortText" })));
        var other = await Other();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/journal-templates/{template.GetProperty("id").GetInt32()}")).StatusCode);
        var borrowed = await other.PostAsJsonAsync("/api/trades", Closed("TPL-BORROW", template.GetProperty("currentVersionId").GetInt32(), new { lesson = "x" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, borrowed.StatusCode);
    }
}
