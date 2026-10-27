using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Manual journaling: drafts, open and closed trades, review, and what reaches analytics
public class TradeJournalEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TradeJournalEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> OwnerClient() =>
        _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

    private static object Draft(string instrument) => new { status = "Draft", tradeDate = "2026-10-26", instrument, session = "London" };

    private static object ClosedEntry(string instrument, string status = "Closed") => new
    {
        status, tradeDate = "2026-10-26", instrument, direction = "Short", session = "London", currency = "USD",
        entryTime = "09:31:00", entryPrice = 1.0825m, stopLoss = 1.0840m, takeProfit = 1.0790m,
        riskAmount = 150m, exitTime = "10:16:00", exitPrice = 1.0800m, pnL = 375m, fees = 7m
    };

    private static async Task<JsonElement> SummaryAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/analytics/summary");

    [Fact]
    public async Task A_draft_saves_with_missing_execution_data_and_stays_out_of_analytics()
    {
        var client = await OwnerClient();
        var before = (await SummaryAsync(client)).GetProperty("totalTrades").GetInt32();

        var response = await client.PostAsJsonAsync("/api/trades", Draft("EURUSD-DRAFT"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("pnL").ValueKind);        // not 0
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("direction").ValueKind);
        Assert.Equal("Monday", draft.GetProperty("dayOfWeek").GetString());
        Assert.Equal(before, (await SummaryAsync(client)).GetProperty("totalTrades").GetInt32());

        var drafts = await client.GetFromJsonAsync<JsonElement>("/api/trades?status=Draft");
        Assert.Contains(drafts.EnumerateArray(), t => t.GetProperty("id").GetInt32() == draft.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Closing_a_draft_needs_its_exit_and_then_counts_with_a_calculated_r()
    {
        var client = await OwnerClient();
        var draft = await (await client.PostAsJsonAsync("/api/trades", Draft("GBPUSD-J"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = draft.GetProperty("id").GetInt32();
        var before = await SummaryAsync(client);

        var incomplete = await client.PutAsJsonAsync($"/api/trades/{id}", new { status = "Closed", tradeDate = "2026-10-26", instrument = "GBPUSD-J" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incomplete.StatusCode);
        Assert.Contains("ExitPrice", await incomplete.Content.ReadAsStringAsync());

        var closed = await client.PutAsJsonAsync($"/api/trades/{id}", ClosedEntry("GBPUSD-J"));
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var trade = await closed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Closed", trade.GetProperty("status").GetString());
        Assert.Equal(2.5m, trade.GetProperty("rr").GetDecimal());              // 375 ÷ 150
        Assert.Equal(45, trade.GetProperty("tradeDuration").GetInt32());       // 09:31 → 10:16
        Assert.Equal(7m, trade.GetProperty("fees").GetDecimal());

        var after = await SummaryAsync(client);
        Assert.Equal(before.GetProperty("totalTrades").GetInt32() + 1, after.GetProperty("totalTrades").GetInt32());
        Assert.Equal(before.GetProperty("totalPnL").GetDecimal() + 375m, after.GetProperty("totalPnL").GetDecimal());
    }

    [Fact]
    public async Task Review_needs_a_closed_trade_and_keeps_the_trader_notes()
    {
        var client = await OwnerClient();
        var draft = await (await client.PostAsJsonAsync("/api/trades", Draft("XAUUSD-J"))).Content.ReadFromJsonAsync<JsonElement>();
        var early = await client.PostAsJsonAsync($"/api/trades/{draft.GetProperty("id").GetInt32()}/review", new { reviewNotes = "Too early" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, early.StatusCode);

        var closed = await (await client.PostAsJsonAsync("/api/trades", ClosedEntry("NAS-J"))).Content.ReadFromJsonAsync<JsonElement>();
        var reviewed = await client.PostAsJsonAsync($"/api/trades/{closed.GetProperty("id").GetInt32()}/review",
            new { reviewNotes = "Valid setup; exited at target as planned." });

        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        var trade = await reviewed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Reviewed", trade.GetProperty("status").GetString());
        Assert.Equal("Valid setup; exited at target as planned.", trade.GetProperty("reviewNotes").GetString());

        // Saving the review as a plain status is refused: reviews go through the review action
        var shortcut = await client.PostAsJsonAsync("/api/trades", ClosedEntry("NAS-J2", status: "Reviewed"));
        Assert.Equal(HttpStatusCode.BadRequest, shortcut.StatusCode);
    }

    [Fact]
    public async Task A_retried_save_with_the_same_key_creates_one_trade()
    {
        var client = await OwnerClient();
        var key = Guid.NewGuid().ToString("N");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/trades") { Content = JsonContent.Create(ClosedEntry("RETRY-J")) };
            request.Headers.Add("Idempotency-Key", key);
            return client.SendAsync(request);
        }));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var trades = await client.GetFromJsonAsync<JsonElement>("/api/trades");
        Assert.Single(trades.EnumerateArray(), t => t.GetProperty("instrument").GetString() == "RETRY-J");
    }

    [Fact]
    public async Task Imported_style_trades_without_a_status_are_still_closed()
    {
        var client = await OwnerClient();

        var created = await client.PostAsJsonAsync("/api/trades", ApiClientExtensions.ValidTrade("LEGACY-J"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var trade = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Closed", trade.GetProperty("status").GetString());
        Assert.False(trade.GetProperty("isImported").GetBoolean());
    }

    [Fact]
    public async Task Another_user_cannot_review_or_see_a_draft()
    {
        var owner = await OwnerClient();
        var other = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);
        var draft = await (await owner.PostAsJsonAsync("/api/trades", Draft("PRIVATE-J"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = draft.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/trades/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/trades/{id}/review", new { reviewNotes = "x" })).StatusCode);
    }
}
