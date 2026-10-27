using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

internal static class ApiClientExtensions
{
    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static async Task<HttpClient> AuthenticateAsync(this HttpClient client, string email, string password)
    {
        var response = await client.LoginAsync(email, password);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    public static object ValidTrade(string instrument = "NQ") => new
    {
        tradeDate = "2026-10-05",
        entryTime = "09:35:00",
        instrument,
        direction = "Long",
        entryPrice = 20100.25m,
        exitPrice = 20140.25m,
        riskAmount = 200m,
        pnL = 400m,
        rr = 2m,
        strategyName = "ORB",
        session = "New York",
        dayOfWeek = "Monday",
        tradeDuration = 25,
        isManualOverride = false
    };
}
