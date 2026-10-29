using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// Phase 6: the Executive Overview's period figures. Each is summed from bank and cash ledger entries by transaction
// type, so each matches the ledger list for that type and those dates, and the movements add up to the change in cash.
public class OverviewEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OverviewEndpointTests(ApiFactory factory) => _factory = factory;

    private static readonly DateOnly ThisMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    private static readonly DateOnly A = ThisMonth.AddMonths(-2);           // the period: two whole months
    private static readonly DateOnly B = ThisMonth.AddMonths(-1);
    private static readonly DateOnly End = ThisMonth.AddDays(-1);
    private static string D(DateOnly month, int day) => month.AddDays(day - 1).ToString("yyyy-MM-dd");
    private static string S(DateOnly date) => date.ToString("yyyy-MM-dd");

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Dictionary<string, JsonElement> Lines(JsonElement currency) =>
        currency.GetProperty("movements").EnumerateArray().ToDictionary(l => l.GetProperty("type").GetString()!);

    [Fact]
    public async Task Period_figures_add_up_to_the_change_in_cash_and_each_matches_its_ledger_list()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var operating = await client.CreateAccountAsync("Overview operating", "USD");
        var reserve = await client.CreateAccountAsync("Overview reserve", "USD");
        var broker = await client.CreateAccountAsync("Overview broker", "USD", "Brokerage");
        var wallet = await client.CreateAccountAsync("Overview wallet", "ZAR", "Cash");

        // Before the period: the starting cash
        await client.RecordAsync("opening-balance", new { accountId = operating, fundId = Unallocated, amount = 1000m, date = D(A.AddMonths(-1), 5), description = "Carried over" });

        // First month: a payout with a fee, an expense, a transfer between cash accounts and one to a brokerage account
        var payout = await Ok(client.PostAsync("/api/payouts", new { source = "Prop firm", currency = "USD", amount = 1400m, requestedOn = D(A, 1) }, Guid.NewGuid().ToString("N")));
        var payoutId = payout.GetProperty("id").GetInt32();
        await Ok(client.PostAsJsonAsync($"/api/payouts/{payoutId}/approve", new { date = D(A, 1) }));
        await Ok(client.PostAsync($"/api/payouts/{payoutId}/receive", new { accountId = operating, amountCredited = 1350m, fee = 50m, date = D(A, 2) }, Guid.NewGuid().ToString("N")));
        await client.RecordAsync("expense", new { accountId = operating, fundId = Unallocated, amount = 300m, date = D(A, 3), description = "Data feed" });
        await client.RecordAsync("transfer", new { fromAccountId = operating, fromFundId = Unallocated, toAccountId = reserve, toFundId = Unallocated, amount = 200m, date = D(A, 4), description = "To reserve" });
        await client.RecordAsync("transfer", new { fromAccountId = operating, fromFundId = Unallocated, toAccountId = broker, toFundId = Unallocated, amount = 100m, date = D(A, 5), description = "Fund the broker" });

        // Second month: an exchange with a fee, an expense reversed, an opening balance recorded late, a brokerage-only expense
        await client.RecordAsync("exchange", new { fromAccountId = operating, fromFundId = Unallocated, amountSent = 100m, toAccountId = wallet, toFundId = Unallocated, amountReceived = 1800m, fee = 2m, feeChargedTo = "Sending", date = D(B, 1), description = "To rand" });
        var mistake = await client.RecordAsync("expense", new { accountId = operating, fundId = Unallocated, amount = 40m, date = D(B, 2), description = "Charged twice" });
        await Ok(client.PostAsync($"/api/transactions/{mistake.GetProperty("id").GetInt32()}/reverse", new { date = D(B, 3), reason = "Duplicate" }, Guid.NewGuid().ToString("N")));
        await client.RecordAsync("opening-balance", new { accountId = reserve, fundId = Continuity, amount = 500m, date = D(B, 4), description = "Found an old balance" });
        await client.RecordAsync("expense", new { accountId = broker, fundId = Unallocated, amount = 10m, date = D(B, 5), description = "Platform fee" });

        // After the period: not counted
        await client.RecordAsync("income", new { accountId = operating, fundId = Unallocated, amount = 999m, date = S(ThisMonth), description = "Next month" });

        var overview = await Ok(client.GetAsync($"/api/overview?from={S(A)}&to={S(End)}"));
        var currencies = overview.GetProperty("currencies").EnumerateArray().ToDictionary(c => c.GetProperty("currency").GetString()!);
        Assert.Equal(["USD", "ZAR"], currencies.Keys);

        var usd = currencies["USD"];
        var lines = Lines(usd);
        Assert.Equal(1000m, usd.GetProperty("cashAtStart").GetDecimal());
        Assert.Equal((1350m, 1400m, -50m, 1), (lines["Income"].GetProperty("amount").GetDecimal(), lines["Income"].GetProperty("gross").GetDecimal(),
            lines["Income"].GetProperty("fees").GetDecimal(), lines["Income"].GetProperty("transactions").GetInt32()));
        Assert.Equal((-340m, 2), (lines["Expense"].GetProperty("amount").GetDecimal(), lines["Expense"].GetProperty("transactions").GetInt32()));   // not the brokerage one
        Assert.Equal((-102m, 1), (lines["CurrencyExchange"].GetProperty("amount").GetDecimal(), lines["CurrencyExchange"].GetProperty("transactions").GetInt32()));
        Assert.Equal((-100m, 2), (lines["Transfer"].GetProperty("amount").GetDecimal(), lines["Transfer"].GetProperty("transactions").GetInt32()));   // cash to cash cancels out
        Assert.Equal((40m, 1), (lines["Reversal"].GetProperty("amount").GetDecimal(), lines["Reversal"].GetProperty("transactions").GetInt32()));
        Assert.Equal(848m, usd.GetProperty("netCashMovement").GetDecimal());
        Assert.Equal(500m, usd.GetProperty("openingBalances").GetProperty("amount").GetDecimal());
        Assert.Equal(2348m, usd.GetProperty("cashAtEnd").GetDecimal());

        // The end of the period agrees with the cash totals as of that day
        var balances = await Ok(client.GetAsync($"/api/balances?asOf={S(End)}"));
        Assert.Equal(2348m, balances.GetProperty("cashTotals").EnumerateArray().Single(t => t.GetProperty("currency").GetString() == "USD").GetProperty("amount").GetDecimal());
        Assert.Equal((0m, 1800m, 1800m), (currencies["ZAR"].GetProperty("cashAtStart").GetDecimal(), currencies["ZAR"].GetProperty("netCashMovement").GetDecimal(), currencies["ZAR"].GetProperty("cashAtEnd").GetDecimal()));

        // By month: income and expenses, and the net movement, per currency
        var months = overview.GetProperty("months").EnumerateArray()
            .ToDictionary(m => (m.GetProperty("start").GetString(), m.GetProperty("currency").GetString()));
        var first = months[(S(A), "USD")];
        Assert.Equal((1350m, -300m, 950m), (first.GetProperty("income").GetDecimal(), first.GetProperty("expenses").GetDecimal(), first.GetProperty("netCashMovement").GetDecimal()));
        var second = months[(S(B), "USD")];
        Assert.Equal((0m, -40m, -102m), (second.GetProperty("income").GetDecimal(), second.GetProperty("expenses").GetDecimal(), second.GetProperty("netCashMovement").GetDecimal()));
        Assert.Equal(1800m, months[(S(B), "ZAR")].GetProperty("netCashMovement").GetDecimal());
        Assert.Equal(4, months.Count);
        Assert.False(overview.GetProperty("monthsTruncated").GetBoolean());

        var received = Assert.Single(overview.GetProperty("payoutsReceived").EnumerateArray());
        Assert.Equal(("USD", 1350m, 1), (received.GetProperty("currency").GetString(), received.GetProperty("credited").GetDecimal(), received.GetProperty("payouts").GetInt32()));

        // Each line is the ledger list for its type, those dates and bank and cash accounts
        async Task<int> Listed(string type, bool cash) =>
            (await Ok(client.GetAsync($"/api/transactions?from={S(A)}&to={S(End)}&type={type}&cash={cash.ToString().ToLowerInvariant()}"))).GetProperty("totalCount").GetInt32();
        Assert.Equal(2, await Listed("Expense", cash: true));
        Assert.Equal(3, await Listed("Expense", cash: false));
        Assert.Equal(2, await Listed("Transfer", cash: true));
        Assert.Equal(1, await Listed("Income", cash: true));
        Assert.Equal(1, await Listed("Reversal", cash: true));
    }

    [Fact]
    public async Task The_default_period_is_this_month_to_date_and_bad_periods_are_refused()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        var overview = await Ok(client.GetAsync("/api/overview"));
        Assert.Equal(S(ThisMonth), overview.GetProperty("from").GetString());
        Assert.Equal(Today, overview.GetProperty("to").GetString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync($"/api/overview?from={S(End)}&to={S(A)}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync("/api/overview?from=2000-01-01&to=2020-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/overview")).StatusCode);
    }
}
