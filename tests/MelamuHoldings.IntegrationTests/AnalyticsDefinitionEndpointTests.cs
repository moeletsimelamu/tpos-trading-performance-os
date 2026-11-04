using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// P8.4 through the API, from saved trades: an undefined profit factor reaches the client as null, and drawdown as an amount
public class AnalyticsDefinitionEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AnalyticsDefinitionEndpointTests(ApiFactory factory) => _factory = factory;

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Closed(string date, decimal pnL, string strategy) => new
    {
        status = "Closed", tradeDate = date, instrument = "FLT", direction = "Long", entryTime = "09:30:00",
        entryPrice = 100m, exitPrice = 101m, pnL, riskAmount = 50m, session = "London", strategyName = strategy
    };

    [Fact]
    public async Task Undefined_is_null_and_drawdown_is_an_amount_with_the_other_figures_as_before()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

        Assert.Equal(JsonValueKind.Null, (await Ok(client.GetAsync("/api/analytics/summary"))).GetProperty("profitFactor").ValueKind);

        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2033-02-01", 300m, "Clean")));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2033-02-02", 100m, "Clean")));

        var noLosses = await Ok(client.GetAsync("/api/analytics/summary"));
        Assert.Equal(JsonValueKind.Null, noLosses.GetProperty("profitFactor").ValueKind);
        Assert.Equal((2, 400m, 0m), (noLosses.GetProperty("totalTrades").GetInt32(), noLosses.GetProperty("totalPnL").GetDecimal(), noLosses.GetProperty("maxDrawdown").GetDecimal()));

        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2033-02-03", -250m, "Mixed")));
        await Ok(client.PostAsJsonAsync("/api/trades", Closed("2033-02-04", 50m, "Mixed")));

        var summary = await Ok(client.GetAsync("/api/analytics/summary"));
        Assert.Equal(1.80m, summary.GetProperty("profitFactor").GetDecimal());   // 450 / 250
        Assert.Equal((4, 200m, 250m), (summary.GetProperty("totalTrades").GetInt32(), summary.GetProperty("totalPnL").GetDecimal(), summary.GetProperty("maxDrawdown").GetDecimal()));

        var curve = (await Ok(client.GetAsync("/api/analytics/equity-curve"))).EnumerateArray().ToList();
        Assert.Equal([300m, 400m, 150m, 200m], curve.Select(p => p.GetProperty("cumulativePnL").GetDecimal()));
        Assert.Equal([0m, 0m, 250m, 200m], curve.Select(p => p.GetProperty("drawdown").GetDecimal()));

        var strategies = (await Ok(client.GetAsync("/api/analytics/by-strategy"))).EnumerateArray().ToDictionary(s => s.GetProperty("strategyName").GetString()!);
        Assert.Equal(JsonValueKind.Null, strategies["Clean"].GetProperty("profitFactor").ValueKind);
        Assert.Equal(0.20m, strategies["Mixed"].GetProperty("profitFactor").GetDecimal());
    }
}
