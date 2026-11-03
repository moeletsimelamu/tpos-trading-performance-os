using System.Net;
using System.Net.Http.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// Phase 7.5: values the database or the calendar cannot hold are refused with a reason, not a server error
public class FinanceInputLimitTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public FinanceInputLimitTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("9999-12")]
    [InlineData("0001-01")]
    public async Task A_budget_month_outside_the_supported_years_is_refused(string month)
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync($"/api/budgets?month={month}")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PostAsJsonAsync("/api/budgets", new { month, category = "Rent", amount = 100m, currency = "ZAR" })).StatusCode);
    }

    [Fact]
    public async Task An_obligation_due_past_the_supported_years_is_refused()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var request = new { name = "Far away", amount = 100m, currency = "ZAR", recurrence = "Monthly", firstDueDate = "9999-12-31" };

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/obligations", request)).StatusCode);
    }

    [Fact]
    public async Task An_amount_too_large_to_store_is_refused()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        const decimal tooLarge = 1_000_000_000_000_000m;   // the most an amount column holds is 15 digits before the point

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PostAsJsonAsync("/api/budgets", new { month = "2026-11", category = "Rent", amount = tooLarge, currency = "ZAR" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PostAsJsonAsync("/api/obligations", new { name = "Huge", amount = tooLarge, currency = "ZAR", recurrence = "Monthly", firstDueDate = Today })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PostAsJsonAsync("/api/planned-purchases", new { name = "Huge", amount = tooLarge, currency = "ZAR", fundId = OperatingCapital })).StatusCode);

        var account = await client.CreateAccountAsync("Limit ZAR", "ZAR");
        var response = await client.PostAsync("/api/transactions/income",
            new { accountId = account, fundId = OperatingCapital, amount = tooLarge, date = Today, description = "Huge" }, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // The largest amount that fits is still accepted
        Assert.True((await client.PostAsJsonAsync("/api/budgets", new { month = "2026-11", category = "Largest", amount = tooLarge - 0.01m, currency = "ZAR" })).IsSuccessStatusCode);
    }
}
