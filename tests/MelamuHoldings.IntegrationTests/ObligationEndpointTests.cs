using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// Phase 7.1 through the API: obligations with payments linked to ledger expenses, and fund minimums and targets
public class ObligationEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ObligationEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    [Fact]
    public async Task An_obligation_is_paid_by_linking_an_expense_and_only_unlinked_unreversed_ones_in_its_currency_are_offered()
    {
        var client = await Owner();
        var zar = await client.CreateAccountAsync("Obligations ZAR", "ZAR");
        var usd = await client.CreateAccountAsync("Obligations USD", "USD");

        // Due five days ago: overdue
        var rent = await Ok(client.PostAsJsonAsync("/api/obligations", new { name = "Office rent", amount = 8500m, currency = "ZAR", category = "Rent", recurrence = "Monthly", firstDueDate = Day(-5) }));
        var id = rent.GetProperty("id").GetInt32();
        Assert.Equal(("Overdue", -5), (rent.GetProperty("standing").GetString(), rent.GetProperty("daysUntilDue").GetInt32()));

        var paid = await client.RecordAsync("expense", new { accountId = zar, fundId = OperatingCapital, amount = 8650m, date = Day(-4), description = "Rent October", category = "Rent" });
        var reversed = await client.RecordAsync("expense", new { accountId = zar, fundId = OperatingCapital, amount = 100m, date = Day(-3), description = "Wrong" });
        await Ok(client.PostAsync($"/api/transactions/{reversed.GetProperty("id").GetInt32()}/reverse", new { date = Day(-3), reason = "Wrong" }, Guid.NewGuid().ToString("N")));
        var dollars = await client.RecordAsync("expense", new { accountId = usd, fundId = OperatingCapital, amount = 50m, date = Day(-3), description = "In dollars" });

        var candidates = (await Ok(client.GetAsync($"/api/obligations/{id}/payment-candidates"))).EnumerateArray().Select(c => c.GetProperty("transactionId").GetInt32()).ToList();
        Assert.Contains(paid.GetProperty("id").GetInt32(), candidates);
        Assert.DoesNotContain(reversed.GetProperty("id").GetInt32(), candidates);
        Assert.DoesNotContain(dollars.GetProperty("id").GetInt32(), candidates);

        var afterPayment = await Ok(client.PostAsJsonAsync($"/api/obligations/{id}/payments", new { transactionId = paid.GetProperty("id").GetInt32() }));
        var payment = Assert.Single(afterPayment.GetProperty("payments").EnumerateArray());
        Assert.Equal((Day(-5), 8650m, false), (payment.GetProperty("dueDate").GetString(), payment.GetProperty("amount").GetProperty("amount").GetDecimal(), payment.GetProperty("reversed").GetBoolean()));
        Assert.Equal(DateOnly.Parse(Day(-5)).AddMonths(1).ToString("yyyy-MM-dd"), afterPayment.GetProperty("dueDate").GetString());
        Assert.DoesNotContain(paid.GetProperty("id").GetInt32(), (await Ok(client.GetAsync($"/api/obligations/{id}/payment-candidates"))).EnumerateArray().Select(c => c.GetProperty("transactionId").GetInt32()));

        // The same expense cannot pay a second obligation; a reversed or other-currency one is refused
        var licence = (await Ok(client.PostAsJsonAsync("/api/obligations", new { name = "Data licence", amount = 300m, currency = "ZAR", recurrence = "None", firstDueDate = Day(10) }))).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/obligations/{licence}/payments", new { transactionId = paid.GetProperty("id").GetInt32() })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/obligations/{licence}/payments", new { transactionId = reversed.GetProperty("id").GetInt32() })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/obligations/{licence}/payments", new { transactionId = dollars.GetProperty("id").GetInt32() })).StatusCode);

        // Reversing the expense after it was linked shows on the payment; undoing the payment puts the date back
        await Ok(client.PostAsync($"/api/transactions/{paid.GetProperty("id").GetInt32()}/reverse", new { date = Day(-1), reason = "Charged to the wrong account" }, Guid.NewGuid().ToString("N")));
        Assert.True((await Ok(client.GetAsync($"/api/obligations/{id}"))).GetProperty("payments")[0].GetProperty("reversed").GetBoolean());
        var undone = await Ok(client.PostAsync($"/api/obligations/{id}/payments/undo", null));
        Assert.Equal((Day(-5), 0), (undone.GetProperty("dueDate").GetString(), undone.GetProperty("payments").GetArrayLength()));

        // Every change is in the audit log
        var audit = (await Ok(client.GetAsync("/api/audit-events?pageSize=100"))).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("entityType").GetString() == "Obligation" && e.GetProperty("entityId").GetString() == id.ToString())
            .Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Equal(["obligation.payment-undone", "obligation.paid", "obligation.created"], audit);
    }

    [Fact]
    public async Task Active_names_are_unique_cancelled_ones_are_listed_only_on_request_and_bad_requests_are_refused()
    {
        var client = await Owner();
        var first = (await Ok(client.PostAsJsonAsync("/api/obligations", new { name = "Insurance", amount = 1200m, currency = "ZAR", recurrence = "Yearly", firstDueDate = Day(30) }))).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/obligations", new { name = "Insurance", amount = 1m, currency = "ZAR", recurrence = "None", firstDueDate = Day(5) })).StatusCode);

        Assert.Equal("Cancelled", (await Ok(client.PostAsync($"/api/obligations/{first}/cancel", null))).GetProperty("status").GetString());
        await Ok(client.PostAsJsonAsync("/api/obligations", new { name = "Insurance", amount = 1300m, currency = "ZAR", recurrence = "Yearly", firstDueDate = Day(30) }));   // the name is free again
        Assert.DoesNotContain((await Ok(client.GetAsync("/api/obligations"))).EnumerateArray(), o => o.GetProperty("id").GetInt32() == first);
        Assert.Contains((await Ok(client.GetAsync("/api/obligations?includeClosed=true"))).EnumerateArray(), o => o.GetProperty("id").GetInt32() == first);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/obligations", new { name = "Bad", amount = 10.005m, currency = "ZAR", recurrence = "None", firstDueDate = Day(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/obligations", new { name = "Bad", amount = 10m, currency = "XYZ", recurrence = "None", firstDueDate = Day(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/obligations", new { name = "Bad", amount = 10m, currency = "ZAR", recurrence = "Weekly", firstDueDate = Day(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/obligations/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/obligations")).StatusCode);
    }

    [Fact]
    public async Task A_fund_s_minimum_and_target_are_saved_returned_and_validated()
    {
        var client = await Owner();
        var saved = await Ok(client.PutAsJsonAsync($"/api/funds/{OperatingCapital}", new { name = "Operating Capital", thresholdCurrency = "ZAR", minimum = 20000m, target = 50000m }));
        Assert.Equal((20000m, "ZAR", 50000m), (saved.GetProperty("minimum").GetProperty("amount").GetDecimal(), saved.GetProperty("minimum").GetProperty("currency").GetString(), saved.GetProperty("target").GetProperty("amount").GetDecimal()));

        var listed = (await Ok(client.GetAsync("/api/funds"))).EnumerateArray().Single(f => f.GetProperty("id").GetInt32() == OperatingCapital);
        Assert.Equal(20000m, listed.GetProperty("minimum").GetProperty("amount").GetDecimal());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/funds/{OperatingCapital}", new { name = "Operating Capital", minimum = 100m })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/funds/{OperatingCapital}", new { name = "Operating Capital", thresholdCurrency = "ZAR", minimum = 500m, target = 100m })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/funds/{Unallocated}", new { name = "Unallocated", thresholdCurrency = "ZAR", minimum = 1m })).StatusCode);

        var cleared = await Ok(client.PutAsJsonAsync($"/api/funds/{OperatingCapital}", new { name = "Operating Capital" }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("minimum").ValueKind);
    }
}
