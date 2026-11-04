using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// P8.1 through the API: prop firm accounts owned per trader, with versioned rules and an append-only history
public class PropAccountEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PropAccountEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static object Rules(decimal? target = 10_000m, decimal maxDrawdown = 10_000m, string model = "Static") => new
    {
        drawdownModel = model, maxDrawdown, dailyLossLimit = 5_000m, profitTarget = target, minimumTradingDays = 4, payoutSplitPercent = 80m,
        purchaseFee = 500m, resetFee = 400m, monthlyFee = (decimal?)null, notes = "No news trading"
    };

    private static object New(string nickname, decimal accountSize = 100_000m, string currency = "USD", object? rules = null) => new
    {
        firm = "Test Firm", nickname, accountSize, currency, phase = "Evaluation", login = "L-1", openedOn = Day(-30), rules = rules ?? Rules(), notes = (string?)null
    };

    private static async Task<int> Create(HttpClient client, string nickname) =>
        (await Ok(client.PostAsJsonAsync("/api/prop-accounts", New(nickname)))).GetProperty("id").GetInt32();

    [Fact]
    public async Task An_account_goes_through_its_phases_on_one_record_with_every_change_kept()
    {
        var client = await Owner();

        var created = await Ok(client.PostAsJsonAsync("/api/prop-accounts", New("Attempt one")));
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal(("Evaluation", "Active", Day(-30), 100_000m), (created.GetProperty("phase").GetString(), created.GetProperty("status").GetString(), created.GetProperty("runStartedOn").GetString(), created.GetProperty("startingBalance").GetProperty("amount").GetDecimal()));
        var rules = created.GetProperty("rules");
        Assert.Equal((1, "Static", 10_000m, 80m), (rules.GetProperty("version").GetInt32(), rules.GetProperty("rules").GetProperty("drawdownModel").GetString(), rules.GetProperty("rules").GetProperty("maxDrawdown").GetDecimal(), rules.GetProperty("rules").GetProperty("payoutSplitPercent").GetDecimal()));

        // The firm changes a rule; the old version stays
        var changed = await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/rules", new { effectiveFrom = Day(-25), rules = Rules(target: 8_000m) }));
        Assert.Equal(2, changed.GetProperty("rules").GetProperty("version").GetInt32());
        Assert.Equal(2, changed.GetProperty("ruleHistory").GetArrayLength());

        await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/status", new { status = "Pending", date = Day(-12), reason = "Waiting for the funded login" }));
        var funded = await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/phase", new { phase = "Funded", date = Day(-10), login = "L-2", startingBalance = 100_000m, rules = Rules(target: null) }));
        Assert.Equal(("Funded", "Active", "L-2", Day(-10)), (funded.GetProperty("phase").GetString(), funded.GetProperty("status").GetString(), funded.GetProperty("login").GetString(), funded.GetProperty("runStartedOn").GetString()));
        Assert.Equal(JsonValueKind.Null, funded.GetProperty("rules").GetProperty("rules").GetProperty("profitTarget").ValueKind);

        await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/status", new { status = "Breached", date = Day(-5), reason = "Daily loss limit" }));
        var reset = await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/reset", new { date = Day(-4), note = "Paid reset" }));
        Assert.Equal(("Funded", "Active", Day(-4)), (reset.GetProperty("phase").GetString(), reset.GetProperty("status").GetString(), reset.GetProperty("runStartedOn").GetString()));

        var corrected = await Ok(client.PutAsJsonAsync($"/api/prop-accounts/{id}", new { firm = "Better Firm", nickname = "Attempt one", accountSize = 100_000m, login = "L-2" }));
        Assert.Equal("Better Firm", corrected.GetProperty("firm").GetString());

        // Read back from the database: the whole story, newest first, with what the account was at each step
        var read = await Ok(client.GetAsync($"/api/prop-accounts/{id}"));
        var history = read.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(["DetailsChanged", "Reset", "StatusChanged", "PhaseChanged", "StatusChanged", "RulesChanged", "Opened"], history.Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal(("Evaluation", "L-1"), (history[^1].GetProperty("phase").GetString(), history[^1].GetProperty("login").GetString()));
        Assert.Equal("Daily loss limit", history[2].GetProperty("note").GetString());
        Assert.Equal([3, 2, 1], read.GetProperty("ruleHistory").EnumerateArray().Select(r => r.GetProperty("version").GetInt32()));
        Assert.Equal(10_000m, read.GetProperty("ruleHistory")[2].GetProperty("rules").GetProperty("profitTarget").GetDecimal());
    }

    [Fact]
    public async Task Accounts_belong_to_one_trader()
    {
        var owner = await Owner();
        var other = await Other();
        var id = await Create(owner, "Private");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/prop-accounts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/prop-accounts/{id}", new { firm = "F", nickname = "N", accountSize = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/prop-accounts/{id}/status", new { status = "Closed", date = Day(0) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/prop-accounts/{id}/reset", new { date = Day(0) })).StatusCode);
        Assert.DoesNotContain((await Ok(other.GetAsync("/api/prop-accounts"))).EnumerateArray(), a => a.GetProperty("id").GetInt32() == id);

        // The other trader can use the same nickname and sees the currencies
        await Ok(other.PostAsJsonAsync("/api/prop-accounts", New("Private")));
        Assert.Contains("ZAR", (await Ok(other.GetAsync("/api/prop-accounts/currencies"))).EnumerateArray().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Prop_accounts_need_a_session()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/prop-accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/prop-accounts/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/prop-accounts/currencies")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/prop-accounts", New("Nobody"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/prop-accounts/1/reset", new { date = Day(0) })).StatusCode);
    }

    [Fact]
    public async Task Open_nicknames_are_unique_and_a_closed_ones_is_free_until_it_is_reopened()
    {
        var client = await Owner();
        var first = await Create(client, "Same name");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/prop-accounts", New("same NAME"))).StatusCode);

        await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{first}/status", new { status = "Closed", date = Day(0) }));
        Assert.DoesNotContain((await Ok(client.GetAsync("/api/prop-accounts"))).EnumerateArray(), a => a.GetProperty("id").GetInt32() == first);
        Assert.Contains((await Ok(client.GetAsync("/api/prop-accounts?includeClosed=true"))).EnumerateArray(), a => a.GetProperty("id").GetInt32() == first);

        await Create(client, "Same name");
        var reopen = await client.PostAsJsonAsync($"/api/prop-accounts/{first}/status", new { status = "Active", date = Day(0), reason = "By mistake" });
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode);
        Assert.Equal("Closed", (await Ok(client.GetAsync($"/api/prop-accounts/{first}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Bad_requests_are_refused_with_the_right_status()
    {
        var client = await Owner();
        var id = await Create(client, "Refusals");

        // 400: missing or malformed
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/prop-accounts", new { firm = "F", nickname = "N", accountSize = 100m, currency = "USD", phase = "Evaluation", openedOn = Day(0) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/prop-accounts", new { firm = "F", nickname = new string('x', 101), accountSize = 100m, currency = "USD", phase = "Evaluation", openedOn = Day(0), rules = Rules() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/prop-accounts/{id}/status", new { status = "Nonsense", date = Day(0) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/prop-accounts/{id}/rules", new { effectiveFrom = Day(0) })).StatusCode);

        // 422: the rules of the domain
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/prop-accounts", New("Bad currency", currency: "XXX"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/prop-accounts", New("Too large", accountSize: 9_999_999_999_999_999m))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/prop-accounts", New("Limit too big", rules: Rules(maxDrawdown: 100_000m)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/prop-accounts/{id}/status", new { status = "Breached", date = Day(0) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/prop-accounts/{id}/phase", new { phase = "Evaluation", date = Day(0), rules = Rules() })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/prop-accounts/{id}/reset", new { date = Day(-99) })).StatusCode);

        // 404
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/prop-accounts/999999")).StatusCode);

        // None of it left a trace
        var read = await Ok(client.GetAsync($"/api/prop-accounts/{id}"));
        Assert.Single(read.GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Prop_accounts_post_nothing_to_the_ledger()
    {
        var client = await Owner();
        var before = (await Ok(client.GetAsync("/api/transactions?pageSize=1"))).GetProperty("totalCount").GetInt32();

        var id = await Create(client, "No money moves");
        await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/reset", new { date = Day(0) }));

        Assert.Equal(before, (await Ok(client.GetAsync("/api/transactions?pageSize=1"))).GetProperty("totalCount").GetInt32());
    }
}
