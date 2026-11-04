using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// Payment cards through the API: recorded on a bank account, owner only, and never a stored card number
public class PaymentCardEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PaymentCardEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_card_is_added_read_back_and_recorded_as_cancelled_with_an_audit_trail()
    {
        var client = await Owner();
        var id = await client.CreateAccountAsync("Cards bank", "ZAR");

        var added = await Ok(client.PostAsJsonAsync($"/api/financial-accounts/{id}/cards", new { kind = "Debit", name = "Business debit", lastFour = "1234" }));
        var card = Assert.Single(added.GetProperty("cards").EnumerateArray());
        Assert.Equal(("Debit", "Business debit", "1234", "Active"), (card.GetProperty("kind").GetString(), card.GetProperty("name").GetString(), card.GetProperty("lastFour").GetString(), card.GetProperty("status").GetString()));
        var cardId = card.GetProperty("id").GetInt32();

        // A card holds no money and changes no balance
        var read = await client.AccountAsync(id);
        Assert.Equal(0m, read.Balance());
        Assert.Equal(1, read.GetProperty("cards").GetArrayLength());

        var cancelled = await Ok(client.PostAsync($"/api/financial-accounts/{id}/cards/{cardId}/cancel", null));
        Assert.Equal("Cancelled", cancelled.GetProperty("cards")[0].GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, cancelled.GetProperty("cards")[0].GetProperty("cancelledAt").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync($"/api/financial-accounts/{id}/cards/{cardId}/cancel", null)).StatusCode);

        var audit = (await Ok(client.GetAsync("/api/audit-events?pageSize=100"))).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("entityType").GetString() == "PaymentCard" && e.GetProperty("entityId").GetString() == cardId.ToString())
            .Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Equal(["card.cancelled", "card.added"], audit);
    }

    [Fact]
    public async Task Closing_an_account_cancels_its_cards()
    {
        var client = await Owner();
        var id = await client.CreateAccountAsync("Closing bank", "ZAR");
        await Ok(client.PostAsJsonAsync($"/api/financial-accounts/{id}/cards", new { kind = "Credit" }));

        var closed = await Ok(client.PostAsync($"/api/financial-accounts/{id}/close", null));

        Assert.Equal("Cancelled", closed.GetProperty("cards")[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/financial-accounts/{id}/cards", new { kind = "Debit" })).StatusCode);
    }

    [Fact]
    public async Task Card_details_that_should_never_be_kept_are_refused()
    {
        var client = await Owner();
        var bank = await client.CreateAccountAsync("Refusing bank", "ZAR");
        var broker = await client.CreateAccountAsync("Refusing broker", "ZAR", "Brokerage");
        async Task<HttpStatusCode> Add(int account, object body) => (await client.PostAsJsonAsync($"/api/financial-accounts/{account}/cards", body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Add(bank, new { kind = "Debit", lastFour = "4111111111111111" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Add(bank, new { kind = "Debit", lastFour = "12a4" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Add(bank, new { kind = "Debit", lastFour = "123" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Add(bank, new { lastFour = "1234" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Add(bank, new { kind = "Nonsense" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Add(broker, new { kind = "Debit" }));
        Assert.Equal(HttpStatusCode.NotFound, await Add(999_999, new { kind = "Debit" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync($"/api/financial-accounts/{bank}/cards/999999/cancel", null)).StatusCode);

        Assert.Equal(0, (await client.AccountAsync(bank)).GetProperty("cards").GetArrayLength());

        // Same digits on another active card
        await Ok(client.PostAsJsonAsync($"/api/financial-accounts/{bank}/cards", new { kind = "Debit", lastFour = "1234" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Add(bank, new { kind = "Credit", lastFour = "1234" }));
    }

}
