using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// Phase 7.3: valuations of brokerage accounts against what the ledger recorded, labelled unrealised
public class InvestmentEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public InvestmentEndpointTests(ApiFactory factory) => _factory = factory;

    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static decimal Amount(JsonElement e, string property) => e.GetProperty(property).GetProperty("amount").GetDecimal();

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Unrealised_is_the_valuation_less_what_the_ledger_recorded_up_to_its_date()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var bank = await client.CreateAccountAsync("Inv bank", "ZAR");
        var broker = await client.CreateAccountAsync("Inv broker", "ZAR", "Brokerage");
        var dollarBroker = await client.CreateAccountAsync("Inv dollar broker", "USD", "Brokerage");
        await client.CreateAccountAsync("Inv idle", "ZAR", "Brokerage");

        await client.RecordAsync("opening-balance", new { accountId = bank, fundId = Unallocated, amount = 100000m, date = Day(-12), description = "Carried over" });
        await client.RecordAsync("opening-balance", new { accountId = broker, fundId = Unallocated, amount = 10000m, date = Day(-10), description = "Existing portfolio" });
        await client.RecordAsync("transfer", new { fromAccountId = bank, fromFundId = Unallocated, toAccountId = broker, toFundId = Unallocated, amount = 20000m, date = Day(-9), description = "Contribute" });
        await client.RecordAsync("income", new { accountId = broker, fundId = Unallocated, amount = 500m, date = Day(-8), description = "Dividend" });
        await client.RecordAsync("expense", new { accountId = broker, fundId = Unallocated, amount = 50m, date = Day(-7), description = "Platform fee" });
        await client.RecordAsync("transfer", new { fromAccountId = broker, fromFundId = Unallocated, toAccountId = bank, toFundId = Unallocated, amount = 5000m, date = Day(-6), description = "Withdraw" });
        var mistaken = await client.RecordAsync("transfer", new { fromAccountId = bank, fromFundId = Unallocated, toAccountId = broker, toFundId = Unallocated, amount = 1000m, date = Day(-6), description = "Wrong amount" });
        await Ok(client.PostAsync($"/api/transactions/{mistaken.GetProperty("id").GetInt32()}/reverse", new { date = Day(-5), reason = "Wrong amount" }, Guid.NewGuid().ToString("N")));
        await client.RecordAsync("exchange", new { fromAccountId = bank, fromFundId = Unallocated, amountSent = 18000m, toAccountId = dollarBroker, toFundId = Unallocated, amountReceived = 1000m, date = Day(-5), description = "Buy dollars to invest" });

        var valuation = await Ok(client.PostAsJsonAsync($"/api/investments/{broker}/valuations", new { valuedOn = Day(-5), value = 27000m, note = "Broker statement" }));
        Assert.Equal((25450m, 1550m), (Amount(valuation, "recordedBalance"), Amount(valuation, "unrealised")));
        await Ok(client.PostAsJsonAsync($"/api/investments/{dollarBroker}/valuations", new { valuedOn = Day(-1), value = 950m }));

        // A contribution after the valuation is counted apart, never as a gain
        await client.RecordAsync("transfer", new { fromAccountId = bank, fromFundId = Unallocated, toAccountId = broker, toFundId = Unallocated, amount = 3000m, date = Day(-2), description = "Contribute again" });

        var investments = await Ok(client.GetAsync("/api/investments"));
        var accounts = investments.GetProperty("accounts").EnumerateArray().ToDictionary(a => a.GetProperty("accountName").GetString()!);
        Assert.Equal(["Inv broker", "Inv dollar broker", "Inv idle"], accounts.Keys);   // brokerage accounts only

        var zar = accounts["Inv broker"];
        Assert.Equal(Day(-5), zar.GetProperty("asOf").GetString());
        Assert.Equal((25000m, 500m, -50m, 25450m), (Amount(zar, "netContributions"), Amount(zar, "recordedIncome"), Amount(zar, "recordedCosts"), Amount(zar, "recordedBalance")));
        Assert.Equal((27000m, 1550m, 2000m), (Amount(zar.GetProperty("latest"), "value"), Amount(zar, "unrealised"), Amount(zar, "gainOnContributions")));
        Assert.Equal((1, 3000m, 28450m), (zar.GetProperty("transactionsSinceValuation").GetInt32(), Amount(zar, "movedSinceValuation"), Amount(zar, "currentRecordedBalance")));

        var usd = accounts["Inv dollar broker"];
        Assert.Equal((1000m, -50m, "USD"), (Amount(usd, "netContributions"), Amount(usd, "unrealised"), usd.GetProperty("currency").GetString()));

        var idle = accounts["Inv idle"];
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (idle.GetProperty("latest").ValueKind, idle.GetProperty("unrealised").ValueKind));

        // Totals per currency, of valued accounts only; never across currencies
        var totals = investments.GetProperty("totals").EnumerateArray()
            .Select(t => (t.GetProperty("currency").GetString(), t.GetProperty("value").GetDecimal(), t.GetProperty("unrealised").GetDecimal(), t.GetProperty("accounts").GetInt32())).ToList();
        Assert.Equal([("USD", 950m, -50m, 1), ("ZAR", 27000m, 1550m, 1)], totals);

        // A valuation never changes a balance: the cash totals and the brokerage ledger balance are as recorded
        var balances = await Ok(client.GetAsync("/api/balances"));
        var brokerBalance = balances.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("id").GetInt32() == broker);
        Assert.Equal(28450m, Amount(brokerBalance, "balance"));

        var history = (await Ok(client.GetAsync($"/api/investments/{broker}/valuations"))).EnumerateArray().ToList();
        Assert.Equal("Broker statement", Assert.Single(history).GetProperty("note").GetString());
    }
}

// In a database of its own, so its accounts do not change the totals above
public class InvestmentValuationRuleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public InvestmentValuationRuleTests(ApiFactory factory) => _factory = factory;

    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Valuations_are_validated_one_per_day_and_deleting_one_is_audited()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var broker = await client.CreateAccountAsync("Rules broker", "ZAR", "Brokerage");
        var bank = await client.CreateAccountAsync("Rules bank", "ZAR");
        async Task<HttpStatusCode> Post(int account, object body) => (await client.PostAsJsonAsync($"/api/investments/{account}/valuations", body)).StatusCode;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(bank, new { valuedOn = Day(0), value = 100m }));       // not a brokerage account
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(broker, new { valuedOn = Day(3), value = 100m }));     // in the future
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(broker, new { valuedOn = Day(0), value = -1m }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(broker, new { valuedOn = Day(0), value = 10.005m }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(broker, new { value = 100m }));
        Assert.Equal(HttpStatusCode.NotFound, await Post(999999, new { valuedOn = Day(0), value = 100m }));

        var created = await Ok(client.PostAsJsonAsync($"/api/investments/{broker}/valuations", new { valuedOn = Day(0), value = 0m }));
        Assert.Equal(HttpStatusCode.Conflict, await Post(broker, new { valuedOn = Day(0), value = 200m }));

        var id = created.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/investments/{bank}/valuations/{id}")).StatusCode);   // not that account's
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/investments/{broker}/valuations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, await Post(broker, new { valuedOn = Day(0), value = 200m }));   // the day is free again

        var audit = (await Ok(client.GetAsync("/api/audit-events?pageSize=100"))).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString()).Where(a => a!.StartsWith("valuation.")).ToList();
        Assert.Equal(["valuation.recorded", "valuation.deleted", "valuation.recorded"], audit);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/investments")).StatusCode);
    }
}
