using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// Phase 7.2: continuity coverage from recorded balances, with what is not counted shown apart, and restoration
// measured against its own fund's target (PLAN.md D6–D8)
public class ContinuityEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ContinuityEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<(string Reason, decimal Amount, string Currency)> NotCounted(JsonElement e) =>
        e.GetProperty("notCounted").EnumerateArray()
            .Select(n => (n.GetProperty("reason").GetString()!, n.GetProperty("amount").GetProperty("amount").GetDecimal(), n.GetProperty("amount").GetProperty("currency").GetString()!))
            .ToList();

    [Fact]
    public async Task Coverage_counts_only_continuity_rand_in_bank_and_cash_accounts_and_shows_the_rest_apart()
    {
        var client = await Owner();
        var restorationId = (await Ok(client.GetAsync("/api/funds"))).EnumerateArray().Single(f => f.GetProperty("name").GetString() == "Trading Restoration").GetProperty("id").GetInt32();

        // The migration points the settings at Business Continuity, 24 months, ZAR, and the new restoration fund
        var initial = await Ok(client.GetAsync("/api/continuity"));
        var settings = initial.GetProperty("settings");
        Assert.Equal((Continuity, 24, "ZAR", restorationId), (settings.GetProperty("continuityFundId").GetInt32(), settings.GetProperty("months").GetInt32(),
            settings.GetProperty("currency").GetString(), settings.GetProperty("restorationFundId").GetInt32()));
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("coverageMonths").ValueKind);   // no essential expense yet, so no target
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("target").ValueKind);

        var operating = await client.CreateAccountAsync("Continuity operating", "ZAR");
        var reserve = await client.CreateAccountAsync("Continuity reserve", "ZAR");
        var broker = await client.CreateAccountAsync("Continuity broker", "ZAR", "Brokerage");
        var payouts = await client.CreateAccountAsync("Continuity payouts", "USD");
        await client.RecordAsync("opening-balance", new { accountId = operating, fundId = Unallocated, amount = 50000m, date = Today, description = "Carried over" });
        await client.RecordAsync("reallocation", new { accountId = operating, fromFundId = Unallocated, toFundId = Continuity, amount = 30000m, date = Today, description = "Earmark continuity" });
        await client.RecordAsync("opening-balance", new { accountId = reserve, fundId = Continuity, amount = 40000m, date = Today, description = "Reserve account" });
        await client.RecordAsync("opening-balance", new { accountId = broker, fundId = Continuity, amount = 5000m, date = Today, description = "Held at the broker" });
        await client.RecordAsync("opening-balance", new { accountId = payouts, fundId = Continuity, amount = 100m, date = Today, description = "Dollar reserve" });
        await client.RecordAsync("opening-balance", new { accountId = payouts, fundId = restorationId, amount = 1000m, date = Today, description = "Restoration capital" });

        // A confirmed allocation still owes continuity 30% of a payout (USD 405): shown, not counted
        var payout = await client.ReceivedPayoutAsync(payouts, 1350m);
        await client.ConfirmAsync(payout.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key()).OkJsonAsync();

        await Ok(client.PutAsJsonAsync($"/api/funds/{restorationId}", new { name = "Trading Restoration", thresholdCurrency = "USD", target = 5000m }));
        await Ok(client.PutAsJsonAsync("/api/continuity/settings", new { continuityFundId = Continuity, months = 24, monthlyEssentialExpense = 10000m, restorationFundId = restorationId }));

        var report = await Ok(client.GetAsync("/api/continuity"));
        Assert.Equal(70000m, report.GetProperty("cash").GetProperty("amount").GetDecimal());               // 30,000 earmarked + 40,000 separated
        Assert.Equal(240000m, report.GetProperty("target").GetProperty("amount").GetDecimal());            // 24 × 10,000
        Assert.Equal(7.0m, report.GetProperty("coverageMonths").GetDecimal());
        Assert.Equal(170000m, report.GetProperty("shortfall").GetProperty("amount").GetDecimal());
        Assert.True(report.GetProperty("belowTarget").GetBoolean());
        Assert.Equal((30000m, 40000m), (report.GetProperty("earmarked").GetProperty("amount").GetDecimal(), report.GetProperty("separated").GetProperty("amount").GetDecimal()));
        var holdings = report.GetProperty("holdings").EnumerateArray().ToDictionary(h => h.GetProperty("accountName").GetString()!, h => h.GetProperty("separated").GetBoolean());
        Assert.Equal(new Dictionary<string, bool> { ["Continuity operating"] = false, ["Continuity reserve"] = true }, holdings);

        var notCounted = NotCounted(report);
        Assert.Contains(notCounted, n => n.Amount == 5000m && n.Currency == "ZAR" && n.Reason.Contains("brokerage"));
        Assert.Contains(notCounted, n => n.Amount == 100m && n.Currency == "USD" && n.Reason.Contains("never converted"));
        Assert.Contains(notCounted, n => n.Amount == 405m && n.Currency == "USD" && n.Reason.StartsWith("Allocated but not yet"));
        Assert.Equal(3, notCounted.Count);

        // Restoration: its own fund, against that fund's target
        var restoration = report.GetProperty("restoration");
        Assert.Equal((1000m, 5000m, 4000m, 20.0m), (restoration.GetProperty("cash").GetProperty("amount").GetDecimal(), restoration.GetProperty("target").GetProperty("amount").GetDecimal(),
            restoration.GetProperty("shortfall").GetProperty("amount").GetDecimal(), restoration.GetProperty("progress").GetDecimal()));
        Assert.Empty(NotCounted(restoration));   // the default policy allocates nothing to it

        // The change is audited
        var audit = (await Ok(client.GetAsync("/api/audit-events?pageSize=100"))).GetProperty("items").EnumerateArray();
        Assert.Contains(audit, e => e.GetProperty("action").GetString() == "continuity.updated");
    }

    [Fact]
    public async Task Settings_are_validated()
    {
        var client = await Owner();
        var restorationId = (await Ok(client.GetAsync("/api/funds"))).EnumerateArray().Single(f => f.GetProperty("name").GetString() == "Trading Restoration").GetProperty("id").GetInt32();
        async Task<HttpStatusCode> Put(object body) => (await client.PutAsJsonAsync("/api/continuity/settings", body)).StatusCode;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Put(new { continuityFundId = Continuity, months = 24, restorationFundId = Continuity }));   // the same fund twice
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Put(new { continuityFundId = Unallocated, months = 24 }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Put(new { continuityFundId = Continuity, months = 24, monthlyEssentialExpense = -1m }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Put(new { continuityFundId = Continuity, months = 24, monthlyEssentialExpense = 10.005m }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Put(new { continuityFundId = 999999, months = 24 }));
        Assert.Equal(HttpStatusCode.BadRequest, await Put(new { continuityFundId = Continuity, months = 0 }));
        Assert.Equal(HttpStatusCode.BadRequest, await Put(new { continuityFundId = Continuity, months = 121 }));

        var saved = await Ok(client.PutAsJsonAsync("/api/continuity/settings", new { continuityFundId = Continuity, months = 12, restorationFundId = (int?)null }));
        Assert.Equal((12, JsonValueKind.Null), (saved.GetProperty("months").GetInt32(), saved.GetProperty("restorationFundId").ValueKind));
        Assert.Equal(JsonValueKind.Null, (await Ok(client.GetAsync("/api/continuity"))).GetProperty("restoration").ValueKind);
        await Ok(client.PutAsJsonAsync("/api/continuity/settings", new { continuityFundId = Continuity, months = 24, restorationFundId = restorationId }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/continuity")).StatusCode);
    }
}
