using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// P8.2 through the API: trades linked to prop firm accounts, balances from the firm's dashboard, and where an account stands
public class PropAccountStandingEndpointTests : IClassFixture<ApiFactory>
{
    private const string CsvHeader = "TradeDate,EntryTime,Instrument,Direction,EntryPrice,ExitPrice,RiskAmount,PnL,RR,Strategy,Session,DayOfWeek,TradeDuration,Notes,IsManualOverride,DeviationNotes";

    private readonly ApiFactory _factory;

    public PropAccountStandingEndpointTests(ApiFactory factory) => _factory = factory;

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

    private static object Rules(string model = "TrailingEndOfDay", decimal? dailyLoss = 3_000m, decimal? target = 8_000m, int? days = 3) => new
    {
        drawdownModel = model, maxDrawdown = 10_000m, dailyLossLimit = dailyLoss, profitTarget = target, minimumTradingDays = days
    };

    private static async Task<int> Account(HttpClient client, string nickname, object? rules = null) =>
        (await Ok(client.PostAsJsonAsync("/api/prop-accounts", new
        {
            firm = "Test Firm", nickname, accountSize = 100_000m, currency = "USD", phase = "Evaluation", openedOn = Day(-30), rules = rules ?? Rules()
        }))).GetProperty("id").GetInt32();

    private static object Closed(string instrument, string date, decimal pnL, int? propAccountId, string? exitDate = null, string? currency = null, string? tradingAccount = null) => new
    {
        status = "Closed", tradeDate = date, exitDate, instrument, direction = "Long", entryTime = "09:30:00", entryPrice = 100m, exitPrice = 101m, pnL,
        session = "London", propAccountId, currency, tradingAccount
    };

    private static JsonElement Figures(JsonElement account) => account.GetProperty("standing").GetProperty("figures");
    private static decimal Balance(JsonElement account) => Figures(account).GetProperty("balance").GetDecimal();

    private static Task<JsonElement> Read(HttpClient client, int id) => Ok(client.GetAsync($"/api/prop-accounts/{id}"));

    [Fact]
    public async Task An_account_stands_where_its_linked_trades_and_entered_balances_put_it()
    {
        var client = await Owner();
        var id = await Account(client, "Worked by hand");

        await Ok(client.PostAsJsonAsync("/api/trades", Closed("HAND", Day(-20), 5_000m, id)));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("HAND", Day(-19), -3_500m, id)));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("HAND", Day(-18), 4_000m, id)));
        // Not counted: open, draft, and on no account
        await Ok(client.PostAsJsonAsync("/api/trades", new { status = "Open", tradeDate = Day(-1), instrument = "HAND", direction = "Long", entryTime = "09:30:00", entryPrice = 100m, session = "London", propAccountId = id }));
        await Ok(client.PostAsJsonAsync("/api/trades", new { status = "Draft", tradeDate = Day(-1), instrument = "HAND", session = "London", propAccountId = id }));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("HAND", Day(-18), 999m, null)));

        // 105,000, then 101,500, then 105,500, which is the high: the level is 95,500 and the room 10,000
        var account = await Read(client, id);
        var f = Figures(account);
        Assert.Equal((105_500m, 5_500m, 3, 3), (f.GetProperty("balance").GetDecimal(), f.GetProperty("profit").GetDecimal(), f.GetProperty("tradesCounted").GetInt32(), f.GetProperty("tradingDays").GetInt32()));
        var drawdown = f.GetProperty("drawdown");
        Assert.Equal((95_500m, 10_000m, "Clear"), (drawdown.GetProperty("level").GetDecimal(), drawdown.GetProperty("room").GetDecimal(), drawdown.GetProperty("standing").GetString()));
        var daily = f.GetProperty("dailyLoss");
        Assert.Equal(("Reached", Day(-19), 3_500m), (daily.GetProperty("standing").GetString(), daily.GetProperty("worstDay").GetString(), daily.GetProperty("worstLoss").GetDecimal()));
        Assert.Equal(Day(-19), Assert.Single(daily.GetProperty("daysPastLimit").EnumerateArray()).GetString());
        Assert.Equal((2_500m, false), (f.GetProperty("target").GetProperty("remaining").GetDecimal(), f.GetProperty("target").GetProperty("reached").GetBoolean()));
        Assert.True(f.GetProperty("minimumDays").GetProperty("reached").GetBoolean());
        Assert.False(f.GetProperty("targetMet").GetBoolean());
        var standing = account.GetProperty("standing");
        Assert.Equal((5, 1, 0), (standing.GetProperty("linkedTrades").GetInt32(), standing.GetProperty("openTrades").GetInt32(), standing.GetProperty("tradesBeforeRun").GetInt32()));

        // The firm's dashboard says 105,000: 500 less than the trades, and the balance moves on from there
        var read = await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-17), balance = 105_000m, note = "From the dashboard" }));
        var entry = Assert.Single(read.GetProperty("balances").EnumerateArray());
        Assert.Equal((105_000m, 105_500m, -500m), (entry.GetProperty("balance").GetProperty("amount").GetDecimal(), entry.GetProperty("expected").GetDecimal(), entry.GetProperty("difference").GetDecimal()));
        Assert.Equal(9_500m, Figures(read).GetProperty("drawdown").GetProperty("room").GetDecimal());

        await Ok(client.PostAsJsonAsync("/api/trades", Closed("HAND", Day(-16), 3_000m, id)));
        account = await Read(client, id);
        Assert.Equal((108_000m, 8_000m), (Balance(account), Figures(account).GetProperty("profit").GetDecimal()));
        Assert.True(Figures(account).GetProperty("targetMet").GetBoolean());

        // Removing the balance takes it off the figures and leaves it in the history: 108,500 from the trades alone
        var removed = await Ok(client.DeleteAsync($"/api/prop-accounts/{id}/balances/{entry.GetProperty("id").GetInt32()}"));
        Assert.Equal(108_500m, Balance(removed));
        Assert.Equal(0, removed.GetProperty("balances").GetArrayLength());
        Assert.Equal(["BalanceRemoved", "BalanceRecorded", "Opened"], removed.GetProperty("history").EnumerateArray().Select(e => e.GetProperty("kind").GetString()));

        // A filter on the account gives the same trades to analytics, and it is offered as an option
        var summary = await Ok(client.GetAsync("/api/analytics/summary?account=Worked%20by%20hand"));
        Assert.Equal((4, 8_500m), (summary.GetProperty("totalTrades").GetInt32(), summary.GetProperty("totalPnL").GetDecimal()));
        Assert.Contains("Worked by hand", (await Ok(client.GetAsync("/api/analytics/filter-options"))).GetProperty("accounts").EnumerateArray().Select(a => a.GetString()));

        // The trade log's account filter works on the name too
        Assert.Equal(6, (await Ok(client.GetAsync("/api/trades"))).EnumerateArray().Count(t => t.GetProperty("tradingAccount").GetString() == "Worked by hand"));
    }

    [Fact]
    public async Task A_trade_counts_on_the_day_it_closed_and_each_phase_or_reset_starts_again()
    {
        var client = await Owner();
        var id = await Account(client, "Runs");

        await Ok(client.PostAsJsonAsync("/api/trades", Closed("RUNS", Day(-12), -3_000m, id, exitDate: Day(-11))));
        var account = await Read(client, id);
        Assert.Equal(Day(-11), Figures(account).GetProperty("dailyLoss").GetProperty("latestDay").GetString());

        var moved = await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/phase", new { phase = "Funded", date = Day(-10), rules = Rules() }));
        Assert.Equal(100_000m, Balance(moved));
        Assert.Equal((0, 1), (Figures(moved).GetProperty("tradesCounted").GetInt32(), moved.GetProperty("standing").GetProperty("tradesBeforeRun").GetInt32()));
        Assert.Equal(0, Figures(moved).GetProperty("dailyLoss").GetProperty("daysPastLimit").GetArrayLength());

        // A balance before the current run is refused, and one kept from before a reset is marked as not applying
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-11), balance = 1m })).StatusCode);
        await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-9), balance = 100_000m }));
        var reset = await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/reset", new { date = Day(-8) }));
        var entry = Assert.Single(reset.GetProperty("balances").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("expected").ValueKind);
    }

    [Fact]
    public async Task A_linked_trade_takes_the_accounts_name_and_currency_and_can_be_moved_off_it()
    {
        var client = await Owner();
        var id = await Account(client, "Link me");

        var trade = await Ok(client.PostAsJsonAsync("/api/trades", Closed("LINK", Day(-5), 500m, id, tradingAccount: "Ignored")));
        var tradeId = trade.GetProperty("id").GetInt32();
        Assert.Equal((id, "Link me", "USD"), (trade.GetProperty("propAccountId").GetInt32(), trade.GetProperty("tradingAccount").GetString(), trade.GetProperty("currency").GetString()));

        // Renaming the account renames it on the trade
        await Ok(client.PutAsJsonAsync($"/api/prop-accounts/{id}", new { firm = "Test Firm", nickname = "Linked", accountSize = 100_000m }));
        Assert.Equal("Linked", (await Ok(client.GetAsync($"/api/trades/{tradeId}"))).GetProperty("tradingAccount").GetString());
        Assert.Equal(100_500m, Balance(await Read(client, id)));

        // Editing the trade moves the balance, and leaving the account off takes it out and says so in its history
        await Ok(client.PutAsJsonAsync($"/api/trades/{tradeId}", Closed("LINK", Day(-5), 700m, id)));
        Assert.Equal(100_700m, Balance(await Read(client, id)));
        var unlinked = await Ok(client.PutAsJsonAsync($"/api/trades/{tradeId}", Closed("LINK", Day(-5), 700m, null, tradingAccount: "My broker")));
        Assert.Equal((JsonValueKind.Null, "My broker"), (unlinked.GetProperty("propAccountId").ValueKind, unlinked.GetProperty("tradingAccount").GetString()));
        Assert.Equal(100_000m, Balance(await Read(client, id)));
        var change = (await Ok(client.GetAsync($"/api/trades/{tradeId}/history")))[0].GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == "TradingAccount");
        Assert.Equal(("Linked", "My broker"), (change.GetProperty("before").GetString(), change.GetProperty("after").GetString()));

        // Deleting a linked trade takes it out, and leaves the account alone
        var other = (await Ok(client.PostAsJsonAsync("/api/trades", Closed("LINK", Day(-5), 900m, id)))).GetProperty("id").GetInt32();
        Assert.Equal(100_900m, Balance(await Read(client, id)));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/trades/{other}")).StatusCode);
        Assert.Equal(100_000m, Balance(await Read(client, id)));
    }

    [Fact]
    public async Task A_trade_can_only_link_to_your_own_open_account_in_its_own_currency()
    {
        var owner = await Owner();
        var other = await Other();
        var id = await Account(owner, "Refusals");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await other.PostAsJsonAsync("/api/trades", Closed("REFUSE", Day(-5), 1m, id))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync("/api/trades", Closed("REFUSE", Day(-5), 1m, 999_999))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync("/api/trades", Closed("REFUSE", Day(-5), 1m, id, currency: "ZAR"))).StatusCode);
        Assert.Equal(0, (await Read(owner, id)).GetProperty("standing").GetProperty("linkedTrades").GetInt32());

        // A closed account takes no new trade, and one already on it can still be corrected
        var kept = (await Ok(owner.PostAsJsonAsync("/api/trades", Closed("REFUSE", Day(-5), 1m, id)))).GetProperty("id").GetInt32();
        await Ok(owner.PostAsJsonAsync($"/api/prop-accounts/{id}/status", new { status = "Closed", date = Day(0) }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync("/api/trades", Closed("REFUSE", Day(-4), 1m, id))).StatusCode);
        await Ok(owner.PutAsJsonAsync($"/api/trades/{kept}", Closed("REFUSE", Day(-5), 2m, id)));
        Assert.Equal(100_002m, Balance(await Read(owner, id)));
    }

    [Fact]
    public async Task Balances_belong_to_the_accounts_trader_and_bad_ones_are_refused()
    {
        var owner = await Owner();
        var other = await Other();
        var id = await Account(owner, "Readings");
        var entry = (await Ok(owner.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-5), balance = 99_000m, drawdownLevel = 90_000m }))).GetProperty("balances")[0].GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-4), balance = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/prop-accounts/{id}/balances/{entry}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-4), balance = 1m })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-4) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-5), balance = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(-4), balance = -1m })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.PostAsJsonAsync($"/api/prop-accounts/{id}/balances", new { asOf = Day(5), balance = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.DeleteAsync($"/api/prop-accounts/{id}/balances/999999")).StatusCode);
        Assert.Single((await Read(owner, id)).GetProperty("balances").EnumerateArray());
    }

    private static Task<HttpResponseMessage> UploadCsv(HttpClient client, string rows, int? propAccountId)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(CsvHeader + "\n" + rows));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { file, "file", "platform.csv" } };
        if (propAccountId is { } id) form.Add(new StringContent(id.ToString()), "propAccountId");
        return client.PostAsync("/api/upload", form);
    }

    [Fact]
    public async Task A_whole_file_can_be_imported_onto_an_account_and_without_one_nothing_changes()
    {
        var client = await Owner();
        var id = await Account(client, "Imported");

        var result = await Ok(UploadCsv(client, $"{Day(-9)},09:00,IMP-A,Long,10,11,10,250,2,,London,Monday,5,,false,\n{Day(-8)},09:00,IMP-A,Long,10,11,10,-50,2,,London,Monday,5,,false,", id));
        Assert.Equal(2, result.GetProperty("parsedRows").GetInt32());

        var account = await Read(client, id);
        Assert.Equal((100_200m, 2), (Balance(account), Figures(account).GetProperty("tradesCounted").GetInt32()));
        var imported = (await Ok(client.GetAsync("/api/trades"))).EnumerateArray().First(t => t.GetProperty("instrument").GetString() == "IMP-A");
        Assert.Equal((id, "USD", true), (imported.GetProperty("propAccountId").GetInt32(), imported.GetProperty("currency").GetString(), imported.GetProperty("isImported").GetBoolean()));

        await Ok(UploadCsv(client, $"{Day(-7)},09:00,IMP-B,Long,10,11,10,99,2,,London,Monday,5,,false,", null));
        var plain = (await Ok(client.GetAsync("/api/trades"))).EnumerateArray().First(t => t.GetProperty("instrument").GetString() == "IMP-B");
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("propAccountId").ValueKind);
        Assert.Equal(100_200m, Balance(await Read(client, id)));

        // Someone else's account, or a closed one, stops the import before anything is read
        var other = await Other();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await UploadCsv(other, $"{Day(-6)},09:00,IMP-C,Long,10,11,10,1,2,,London,Monday,5,,false,", id)).StatusCode);
        Assert.DoesNotContain((await Ok(other.GetAsync("/api/trades"))).EnumerateArray(), t => t.GetProperty("instrument").GetString() == "IMP-C");
    }

    [Fact]
    public async Task Editing_a_trade_does_not_disturb_the_account_it_is_on()
    {
        var client = await Owner();
        var id = await Account(client, "Undisturbed");
        var tradeId = (await Ok(client.PostAsJsonAsync("/api/trades", Closed("KEEP", Day(-5), 10m, id)))).GetProperty("id").GetInt32();
        var before = await Read(client, id);

        await Ok(client.PutAsJsonAsync($"/api/trades/{tradeId}", Closed("KEEP", Day(-5), 20m, id)));

        var after = await Read(client, id);
        Assert.Equal(before.GetProperty("history").GetArrayLength(), after.GetProperty("history").GetArrayLength());
        Assert.Equal(before.GetProperty("updatedAt").ValueKind, after.GetProperty("updatedAt").ValueKind);
        Assert.Equal(100_020m, Balance(after));
    }
}
