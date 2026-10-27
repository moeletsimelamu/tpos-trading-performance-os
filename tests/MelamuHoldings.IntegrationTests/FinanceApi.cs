using System.Net.Http.Json;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Small helpers for driving the finance API in tests
internal static class FinanceApi
{
    public static async Task<HttpResponseMessage> PostAsync(this HttpClient client, string path, object body, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<int> CreateAccountAsync(this HttpClient client, string name, string currency, string type = "Bank")
    {
        var response = await client.PostAsJsonAsync("/api/financial-accounts",
            new { name, type, currency, ownership = "Company", institution = "Test Bank" });
        response.EnsureSuccessStatusCode();
        return (await response.JsonAsync()).GetProperty("id").GetInt32();
    }

    public static async Task<JsonElement> RecordAsync(this HttpClient client, string kind, object body)
    {
        var response = await client.PostAsync($"/api/transactions/{kind}", body, Guid.NewGuid().ToString("N"));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{kind} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.JsonAsync();
    }

    public static async Task<JsonElement> AccountAsync(this HttpClient client, int id) =>
        await (await client.GetAsync($"/api/financial-accounts/{id}")).JsonAsync();

    public static decimal Balance(this JsonElement account) => account.GetProperty("balance").GetProperty("amount").GetDecimal();

    public static decimal FundBalance(this JsonElement account, int fundId) =>
        account.GetProperty("funds").EnumerateArray()
            .Where(f => f.GetProperty("fundId").GetInt32() == fundId)
            .Select(f => f.GetProperty("balance").GetProperty("amount").GetDecimal())
            .SingleOrDefault();

    public static string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    // Seeded fund ids (FundConfiguration)
    public const int Unallocated = 1, OperatingCapital = 2, Continuity = 3;
}
