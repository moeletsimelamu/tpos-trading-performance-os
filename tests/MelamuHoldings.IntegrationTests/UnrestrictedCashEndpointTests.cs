using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// PLAN.md D9: cash free to use, worked out by hand below, in a database of its own because every total must be exact
public class UnrestrictedCashEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public UnrestrictedCashEndpointTests(ApiFactory factory) => _factory = factory;

    private static async Task<int> AccountAsync(HttpClient client, string name, string currency, string ownership)
    {
        var account = await client.PostAsJsonAsync("/api/financial-accounts", new { name, type = "Bank", currency, ownership }).OkJsonAsync();
        return account.Id();
    }

    private static decimal Figure(JsonElement currency, string name) => currency.GetProperty(name).GetDecimal();

    private static JsonElement Currency(JsonElement report, string code) =>
        report.GetProperty("currencies").EnumerateArray().Single(c => c.GetProperty("currency").GetString() == code);

    [Fact]
    public async Task It_counts_company_cash_in_two_funds_less_what_is_spoken_for_and_lists_the_rest_apart()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        var operating = await client.CreateAccountAsync("Operating ZAR", "ZAR");
        var payouts = await client.CreateAccountAsync("Payouts USD", "USD");
        var broker = await client.CreateAccountAsync("Broker ZAR", "ZAR", "Brokerage");
        var personal = await AccountAsync(client, "Personal ZAR", "ZAR", "Personal");
        var personalUsd = await AccountAsync(client, "Personal USD", "USD", "Personal");
        var unstated = await AccountAsync(client, "Unstated ZAR", "ZAR", "Unspecified");

        Task Open(int account, int fund, decimal amount) =>
            client.RecordAsync("opening-balance", new { accountId = account, fundId = fund, amount, date = Today, description = "Carried over" });
        await Open(operating, Unallocated, 50000m);
        await Open(operating, OperatingCapital, 20000m);
        await Open(operating, Continuity, 30000m);      // another fund: not counted, and not listed either
        await Open(broker, Unallocated, 3000m);
        await Open(personal, Unallocated, 7000m);
        await Open(unstated, OperatingCapital, 1000m);

        await client.PutAsJsonAsync($"/api/funds/{OperatingCapital}", new { name = "Operating Capital", thresholdCurrency = "ZAR", minimum = 5000m }).OkJsonAsync();

        // Only committed purchases from a counted fund reserve money
        async Task<int> Purchase(string name, decimal amount, int fundId) =>
            (await client.PostAsJsonAsync("/api/planned-purchases", new { name, amount, currency = "ZAR", fundId }).OkJsonAsync()).Id();
        await client.PostAsync($"/api/planned-purchases/{await Purchase("Committed laptop", 4000m, OperatingCapital)}/commit", null).OkJsonAsync();
        await Purchase("Only proposed", 9000m, OperatingCapital);
        await client.PostAsync($"/api/planned-purchases/{await Purchase("From the wealth fund", 2000m, PayoutApi.WealthFund)}/commit", null).OkJsonAsync();

        // Due within 30 days, shown and not deducted; the later one is left out
        await client.PostAsJsonAsync("/api/obligations", new { name = "Rent", amount = 8000m, currency = "ZAR", recurrence = "Monthly", firstDueDate = Today }).OkJsonAsync();
        await client.PostAsJsonAsync("/api/obligations", new { name = "Insurance", amount = 600m, currency = "ZAR", recurrence = "Yearly",
            firstDueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(31).ToString("yyyy-MM-dd") }).OkJsonAsync();

        // USD 1,350 allocated 40/30/20/10, with continuity's 405 earmarked: 270 + 135 are still owed to other funds,
        // and Operating Capital's 540 is not a claim on this money
        var allocated = await client.ReceivedPayoutAsync(payouts, 1350m);
        var allocation = await client.ConfirmAsync(allocated.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key()).OkJsonAsync();
        await client.SettleAsync(allocation.Id(), allocation.Line(Continuity).Id(), new { method = "Earmark", amount = 405m, date = Today }).OkJsonAsync();

        // USD 500 received with no allocation yet, and USD 100 whose allocation was cancelled: neither is free
        await client.ReceivedPayoutAsync(payouts, 500m);
        var cancelled = await client.ReceivedPayoutAsync(payouts, 100m);
        var withdrawn = await client.ConfirmAsync(cancelled.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key()).OkJsonAsync();
        await client.PostAsync($"/api/allocations/{withdrawn.Id()}/cancel", new { reason = "Changed my mind" }, PayoutApi.Key()).OkJsonAsync();

        // Received into a personal account: never in the figure, so nothing comes off for it, and listed apart
        var intoPersonal = await client.ReceivedPayoutAsync(personalUsd, 200m);
        await client.ConfirmAsync(intoPersonal.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key()).OkJsonAsync();

        var report = await client.GetAsync("/api/unrestricted-cash").OkJsonAsync();

        var zar = Currency(report, "ZAR");
        Assert.Equal(70000m, Figure(zar, "held"));              // 50,000 + 20,000
        Assert.Equal(5000m, Figure(zar, "operatingMinimum"));
        Assert.Equal(0m, Figure(zar, "owedToOtherFunds"));
        Assert.Equal(0m, Figure(zar, "payoutsNotAllocated"));
        Assert.Equal(4000m, Figure(zar, "committedPurchases"));
        Assert.Equal(61000m, Figure(zar, "amount"));
        Assert.Equal(8000m, Figure(zar, "obligationsDue"));
        Assert.Equal(53000m, Figure(zar, "afterObligations"));
        Assert.Equal(["Operating ZAR/Unallocated", "Operating ZAR/Operating Capital"],
            zar.GetProperty("holdings").EnumerateArray().Select(h => $"{h.GetProperty("accountName").GetString()}/{h.GetProperty("fundName").GetString()}").ToList());

        var usd = Currency(report, "USD");
        Assert.Equal(1545m, Figure(usd, "held"));               // 1,350 − 405 earmarked + 500 + 100
        Assert.Equal(0m, Figure(usd, "operatingMinimum"));      // the minimum is in rand and is never converted
        Assert.Equal(405m, Figure(usd, "owedToOtherFunds"));    // 270 + 135
        Assert.Equal(600m, Figure(usd, "payoutsNotAllocated")); // 500 + 100
        Assert.Equal(540m, Figure(usd, "amount"));              // Operating Capital's share of the allocated payout
        Assert.Equal(0m, Figure(usd, "obligationsDue"));
        Assert.Equal(540m, Figure(usd, "afterObligations"));

        var notCounted = report.GetProperty("notCounted").EnumerateArray()
            .Select(n => (n.GetProperty("accountName").GetString(), n.GetProperty("amount").GetProperty("amount").GetDecimal(), n.GetProperty("reason").GetString()))
            .ToList();
        Assert.Equal(
            [("Broker ZAR", 3000m, "In a brokerage account, not bank or cash"), ("Personal USD", 200m, "In a personal account"),
             ("Personal ZAR", 7000m, "In a personal account"), ("Unstated ZAR", 1000m, "The account's ownership is not stated")],
            notCounted);
    }
}
