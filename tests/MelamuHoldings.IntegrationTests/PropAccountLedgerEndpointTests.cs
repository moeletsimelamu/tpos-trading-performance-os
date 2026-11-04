using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// P8.3 through the API: fees and payouts linked to a prop firm account, and which accounts cost money without paying back
public class PropAccountLedgerEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PropAccountLedgerEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Key() => Guid.NewGuid().ToString("N");
    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");
    private static int Id(JsonElement element) => element.GetProperty("id").GetInt32();

    private static async Task<int> PropAccount(HttpClient client, string nickname, string currency = "USD") =>
        Id(await Ok(client.PostAsJsonAsync("/api/prop-accounts", new
        {
            firm = "Test Firm", nickname, accountSize = 100_000m, currency, phase = "Funded", openedOn = Day(-30),
            rules = new { drawdownModel = "None" }
        })));

    private static async Task<int> Expense(HttpClient client, int account, decimal amount, string description = "Firm fee") =>
        Id(await client.RecordAsync("expense", new { accountId = account, fundId = OperatingCapital, amount, date = Day(-5), description }));

    private static async Task<int> Payout(HttpClient client, decimal amount, int? propFirmAccountId = null) =>
        Id(await Ok(client.PostAsync("/api/payouts", new { source = "Test Firm", currency = "USD", amount, requestedOn = Day(-3), propFirmAccountId }, Key())));

    private static Task<JsonElement> Approve(HttpClient client, int payout) =>
        Ok(client.PostAsJsonAsync($"/api/payouts/{payout}/approve", new { date = Day(-2) }));

    private static Task<JsonElement> Receive(HttpClient client, int payout, int account, decimal credited) =>
        Ok(client.PostAsync($"/api/payouts/{payout}/receive", new { accountId = account, amountCredited = credited, date = Day(-1) }, Key()));

    private static JsonElement Row(JsonElement report, int accountId) =>
        report.GetProperty("accounts").EnumerateArray().Single(r => r.GetProperty("accountId").GetInt32() == accountId);

    [Fact]
    public async Task An_account_goes_from_fees_without_payouts_to_paying_and_no_money_moves_for_any_link()
    {
        var client = await Owner();
        var bank = await client.CreateAccountAsync("Prop fees USD", "USD");
        await client.RecordAsync("income", new { accountId = bank, fundId = Unallocated, amount = 5_000m, date = Day(-10), description = "Opening money" });
        var id = await PropAccount(client, "Story");

        var empty = await Ok(client.GetAsync($"/api/prop-accounts/{id}/ledger"));
        Assert.Equal("NothingLinked", empty.GetProperty("economics").GetProperty("outcome").GetString());

        var purchase = await Expense(client, bank, 500m, "Challenge purchase");
        var reset = await Expense(client, bank, 200m, "Reset");
        var balanceBefore = (await client.AccountAsync(bank)).Balance();

        // Offered until linked, then not
        var offered = (await Ok(client.GetAsync($"/api/prop-accounts/{id}/ledger/fee-candidates"))).EnumerateArray().Select(c => c.GetProperty("transactionId").GetInt32()).ToList();
        Assert.Contains(purchase, offered);
        await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/ledger/fees", new { transactionId = purchase, kind = "Purchase" }));
        var linked = await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/ledger/fees", new { transactionId = reset, kind = "Reset" }));
        Assert.DoesNotContain(purchase, (await Ok(client.GetAsync($"/api/prop-accounts/{id}/ledger/fee-candidates"))).EnumerateArray().Select(c => c.GetProperty("transactionId").GetInt32()));

        var economics = linked.GetProperty("economics");
        Assert.Equal("FeesWithoutPayouts", economics.GetProperty("outcome").GetString());
        var line = Assert.Single(economics.GetProperty("lines").EnumerateArray());
        Assert.Equal(("USD", 700m, 0m, -700m), (line.GetProperty("currency").GetString(), line.GetProperty("fees").GetDecimal(), line.GetProperty("payoutsReceived").GetDecimal(), line.GetProperty("net").GetDecimal()));
        Assert.Equal(balanceBefore, (await client.AccountAsync(bank)).Balance());

        // A payout on its way is apart from what was received
        var payout = await Payout(client, 1_000m, id);
        var read = await Ok(client.GetAsync($"/api/payouts/{payout}"));
        Assert.Equal((id, "Story"), (read.GetProperty("propFirmAccountId").GetInt32(), read.GetProperty("propAccountName").GetString()));
        await Approve(client, payout);
        var waiting = await Ok(client.GetAsync($"/api/prop-accounts/{id}/ledger"));
        Assert.Equal("FeesWithoutPayouts", waiting.GetProperty("economics").GetProperty("outcome").GetString());
        Assert.Equal(1_000m, Assert.Single(waiting.GetProperty("economics").GetProperty("awaiting").EnumerateArray()).GetProperty("amount").GetDecimal());

        // Received, it counts for what arrived
        await Receive(client, payout, bank, 950m);
        var report = await Ok(client.GetAsync("/api/prop-accounts/fees-and-payouts"));
        var row = Row(report, id).GetProperty("economics");
        Assert.Equal("PayoutsReceived", row.GetProperty("outcome").GetString());
        var received = Assert.Single(row.GetProperty("lines").EnumerateArray());
        Assert.Equal((700m, 950m, 250m), (received.GetProperty("fees").GetDecimal(), received.GetProperty("payoutsReceived").GetDecimal(), received.GetProperty("net").GetDecimal()));
        Assert.Empty(row.GetProperty("awaiting").EnumerateArray());

        // Undoing the receipt takes it back out
        await Ok(client.PostAsync($"/api/payouts/{payout}/undo-receipt", new { date = Day(0), reason = "Wrong account" }, Key()));
        Assert.Equal("FeesWithoutPayouts", Row(await Ok(client.GetAsync("/api/prop-accounts/fees-and-payouts")), id).GetProperty("economics").GetProperty("outcome").GetString());

        // A reversed fee is shown but not counted
        await Ok(client.PostAsync($"/api/transactions/{reset}/reverse", new { date = Day(0), reason = "Refunded" }, Key()));
        var afterReversal = await Ok(client.GetAsync($"/api/prop-accounts/{id}/ledger"));
        Assert.Equal(500m, Assert.Single(afterReversal.GetProperty("economics").GetProperty("lines").EnumerateArray()).GetProperty("fees").GetDecimal());
        Assert.True(afterReversal.GetProperty("fees").EnumerateArray().Single(f => f.GetProperty("transactionId").GetInt32() == reset).GetProperty("reversed").GetBoolean());

        // Removing a link leaves the expense in the ledger
        var feeId = afterReversal.GetProperty("fees").EnumerateArray().Single(f => f.GetProperty("transactionId").GetInt32() == purchase).GetProperty("id").GetInt32();
        var balance = (await client.AccountAsync(bank)).Balance();
        var unlinked = await Ok(client.DeleteAsync($"/api/prop-accounts/{id}/ledger/fees/{feeId}"));
        Assert.Single(unlinked.GetProperty("fees").EnumerateArray());
        Assert.Equal(balance, (await client.AccountAsync(bank)).Balance());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/transactions/{purchase}")).StatusCode);

        var audit = (await Ok(client.GetAsync("/api/audit-events?pageSize=100"))).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("entityType").GetString() == "PropFirmAccount" && e.GetProperty("entityId").GetString() == id.ToString())
            .Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Equal(["propaccount.fee_unlinked", "propaccount.fee_linked", "propaccount.fee_linked"], audit);
    }

    [Fact]
    public async Task A_payout_is_linked_after_the_fact_counted_in_the_currency_it_arrived_in_and_unlinked_payouts_are_reported()
    {
        var client = await Owner();
        var zar = await client.CreateAccountAsync("Prop payouts ZAR", "ZAR");
        var usd = await client.CreateAccountAsync("Prop costs USD", "USD");
        await client.RecordAsync("income", new { accountId = usd, fundId = Unallocated, amount = 1_000m, date = Day(-10), description = "Opening" });
        var id = await PropAccount(client, "Later link");
        var unlinkedBefore = (await Ok(client.GetAsync("/api/prop-accounts/fees-and-payouts"))).GetProperty("unlinkedReceivedPayouts").GetInt32();

        var payout = await Payout(client, 1_000m);
        await Approve(client, payout);
        await Receive(client, payout, zar, 18_000m);
        Assert.Equal(unlinkedBefore + 1, (await Ok(client.GetAsync("/api/prop-accounts/fees-and-payouts"))).GetProperty("unlinkedReceivedPayouts").GetInt32());
        Assert.Contains(payout, (await Ok(client.GetAsync($"/api/prop-accounts/{id}/ledger/payout-candidates"))).EnumerateArray().Select(Id));

        var balance = (await client.AccountAsync(zar)).Balance();
        await Ok(client.PostAsync($"/api/prop-accounts/{id}/ledger/payouts/{payout}", null));
        await Ok(client.PostAsJsonAsync($"/api/prop-accounts/{id}/ledger/fees", new { transactionId = await Expense(client, usd, 500m), kind = "Purchase" }));

        Assert.Equal(balance, (await client.AccountAsync(zar)).Balance());
        Assert.DoesNotContain(payout, (await Ok(client.GetAsync($"/api/prop-accounts/{id}/ledger/payout-candidates"))).EnumerateArray().Select(Id));

        var report = await Ok(client.GetAsync("/api/prop-accounts/fees-and-payouts"));
        Assert.Equal(unlinkedBefore, report.GetProperty("unlinkedReceivedPayouts").GetInt32());
        var lines = Row(report, id).GetProperty("economics").GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(("USD", 500m, 0m), (lines[0].GetProperty("currency").GetString(), lines[0].GetProperty("fees").GetDecimal(), lines[0].GetProperty("payoutsReceived").GetDecimal()));
        Assert.Equal(("ZAR", 0m, 18_000m), (lines[1].GetProperty("currency").GetString(), lines[1].GetProperty("fees").GetDecimal(), lines[1].GetProperty("payoutsReceived").GetDecimal()));

        var removed = await Ok(client.DeleteAsync($"/api/prop-accounts/{id}/ledger/payouts/{payout}"));
        Assert.Empty(removed.GetProperty("payouts").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, (await Ok(client.GetAsync($"/api/payouts/{payout}"))).GetProperty("propFirmAccountId").ValueKind);
    }

    [Fact]
    public async Task What_is_not_a_fee_or_is_already_linked_is_refused()
    {
        var client = await Owner();
        var bank = await client.CreateAccountAsync("Prop refusals", "USD");
        var income = await client.RecordAsync("income", new { accountId = bank, fundId = Unallocated, amount = 5_000m, date = Day(-10), description = "In" });
        var first = await PropAccount(client, "Refusals one");
        var second = await PropAccount(client, "Refusals two");
        async Task<HttpStatusCode> Link(int account, int transactionId, string kind = "Other") =>
            (await client.PostAsJsonAsync($"/api/prop-accounts/{account}/ledger/fees", new { transactionId, kind })).StatusCode;

        var expense = await Expense(client, bank, 100m);
        var reversed = await Expense(client, bank, 50m);
        await Ok(client.PostAsync($"/api/transactions/{reversed}/reverse", new { date = Day(0), reason = "Mistake" }, Key()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Link(first, Id(income)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Link(first, reversed));
        Assert.DoesNotContain(reversed, (await Ok(client.GetAsync($"/api/prop-accounts/{first}/ledger/fee-candidates"))).EnumerateArray().Select(c => c.GetProperty("transactionId").GetInt32()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Link(first, 999_999));
        Assert.Equal(HttpStatusCode.BadRequest, await Link(first, expense, "Nonsense"));
        Assert.Equal(HttpStatusCode.NotFound, await Link(999_999, expense));

        Assert.Equal(HttpStatusCode.OK, await Link(first, expense));
        Assert.Equal(HttpStatusCode.Conflict, await Link(first, expense));
        Assert.Equal(HttpStatusCode.Conflict, await Link(second, expense));
        var feeId = (await Ok(client.GetAsync($"/api/prop-accounts/{first}/ledger"))).GetProperty("fees")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/prop-accounts/{second}/ledger/fees/{feeId}")).StatusCode);

        var payout = await Payout(client, 100m, first);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync($"/api/prop-accounts/{second}/ledger/payouts/{payout}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/prop-accounts/{second}/ledger/payouts/{payout}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/prop-accounts/{first}/ledger/payouts/999999", null)).StatusCode);

        var rejected = await Payout(client, 100m);
        await Ok(client.PostAsJsonAsync($"/api/payouts/{rejected}/reject", new { date = Day(0), reason = "Rule breach" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync($"/api/prop-accounts/{first}/ledger/payouts/{rejected}", null)).StatusCode);
        Assert.DoesNotContain(rejected, (await Ok(client.GetAsync($"/api/prop-accounts/{first}/ledger/payout-candidates"))).EnumerateArray().Select(Id));
    }

    [Fact]
    public async Task Another_traders_account_is_out_of_reach_and_missing_from_their_report()
    {
        var owner = await Owner();
        var other = await Other();
        var bank = await owner.CreateAccountAsync("Prop isolation", "USD");
        await owner.RecordAsync("income", new { accountId = bank, fundId = Unallocated, amount = 1_000m, date = Day(-10), description = "In" });
        var mine = await PropAccount(owner, "Isolated");
        var expense = await Expense(owner, bank, 100m);
        var payout = await Payout(owner, 100m);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/prop-accounts/{mine}/ledger")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/prop-accounts/{mine}/ledger/fee-candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/prop-accounts/{mine}/ledger/fees", new { transactionId = expense, kind = "Other" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/prop-accounts/{mine}/ledger/payouts/{payout}", null)).StatusCode);
        Assert.DoesNotContain(mine, (await Ok(other.GetAsync("/api/prop-accounts/fees-and-payouts"))).GetProperty("accounts").EnumerateArray().Select(r => r.GetProperty("accountId").GetInt32()));

        // A payout cannot be recorded against someone else's account either
        var refused = await other.PostAsync("/api/payouts", new { source = "Firm", currency = "USD", amount = 10m, requestedOn = Day(0), propFirmAccountId = mine }, Key());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
    }
}
