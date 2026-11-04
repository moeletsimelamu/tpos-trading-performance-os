using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// The business's accounts as the owner intends to run them: funds apart from accounts, one fund spanning several
// accounts, and company money apart from personal money. In a database of its own, so its totals are exact.
public class AccountStructureEndpointTests : IClassFixture<ApiFactory>
{
    private const int WealthFund = 4, OwnersDistribution = 5;

    private readonly ApiFactory _factory;

    public AccountStructureEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset).ToString("yyyy-MM-dd");

    private static async Task<int> Account(HttpClient client, string name, string type, string ownership, string category = "NotStated", string tax = "NotStated", int? homeFundId = null) =>
        (await Ok(client.PostAsJsonAsync("/api/financial-accounts", new
        {
            name, type, currency = "ZAR", ownership, category, taxTreatment = tax, homeFundId, institution = name.Split(' ')[0], reference = "1234", openedOn = Day(-400)
        }))).GetProperty("id").GetInt32();

    private static Task<JsonElement> Opening(HttpClient client, int accountId, int fundId, decimal amount) =>
        client.RecordAsync("opening-balance", new { accountId, fundId, amount, date = Day(-30), description = "Opening balance" });

    private static List<(string Ownership, string Currency, decimal Amount)> OtherCash(JsonElement balances) => balances.GetProperty("otherCash").EnumerateArray()
        .Select(o => (o.GetProperty("ownership").GetString()!, o.GetProperty("amount").GetProperty("currency").GetString()!, o.GetProperty("amount").GetProperty("amount").GetDecimal())).ToList();

    [Fact]
    public async Task Business_cash_counts_company_accounts_only_and_a_fund_spans_its_accounts()
    {
        var client = await Owner();
        var gold = await Account(client, "FNB Gold Business", "Bank", "Company", "Current", homeFundId: OperatingCapital);
        var goalSave = await Account(client, "TymeBank GoalSave", "Bank", "Company", "Savings", homeFundId: Continuity);
        var tfsa = await Account(client, "EasyEquities TFSA", "Brokerage", "Personal", "Investment", "TaxFree", WealthFund);
        var normal = await Account(client, "EasyEquities Normal", "Brokerage", "Company", "Investment", "Taxable", WealthFund);
        var aspire = await Account(client, "FNB Aspire Current", "Bank", "Personal", "Current", homeFundId: OwnersDistribution);
        var unstated = await Account(client, "Unstated Bank", "Bank", "Unspecified");

        await Opening(client, gold, OperatingCapital, 10_000m);
        await Opening(client, goalSave, Continuity, 5_000m);
        await Opening(client, tfsa, WealthFund, 3_000m);
        await Opening(client, normal, WealthFund, 1_500m);
        await Opening(client, aspire, OwnersDistribution, 2_000m);
        await Opening(client, unstated, Unallocated, 700m);

        // Cash is the two company bank accounts. Personal and unstated money is reported apart, and brokerages not at all.
        var balances = await Ok(client.GetAsync("/api/balances"));
        Assert.Equal(15_000m, Assert.Single(balances.GetProperty("cashTotals").EnumerateArray()).GetProperty("amount").GetDecimal());
        Assert.Equal([("Personal", "ZAR", 2_000m), ("Unspecified", "ZAR", 700m)], OtherCash(balances));

        // The Wealth Fund is one purpose across two accounts: its total is not either account's balance
        var funds = balances.GetProperty("funds").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetInt32());
        Assert.Equal(4_500m, Assert.Single(funds[WealthFund].GetProperty("balances").EnumerateArray()).GetProperty("amount").GetDecimal());
        Assert.Equal(["EasyEquities Normal", "EasyEquities TFSA"], funds[WealthFund].GetProperty("accounts").EnumerateArray().Select(a => a.GetProperty("name").GetString()));

        // Each account says what it is
        var accounts = balances.GetProperty("accounts").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetInt32());
        var isa = accounts[tfsa];
        Assert.Equal(("Personal", "Investment", "TaxFree", "1234", Day(-400), "Wealth Fund"), (
            isa.GetProperty("ownership").GetString(), isa.GetProperty("category").GetString(), isa.GetProperty("taxTreatment").GetString(),
            isa.GetProperty("reference").GetString(), isa.GetProperty("openedOn").GetString(), isa.GetProperty("homeFundName").GetString()));
        Assert.Equal("Taxable", accounts[normal].GetProperty("taxTreatment").GetString());
        var investments = (await Ok(client.GetAsync("/api/investments"))).GetProperty("accounts").EnumerateArray().ToDictionary(a => a.GetProperty("accountId").GetInt32());
        Assert.Equal(("TaxFree", "Taxable"), (investments[tfsa].GetProperty("taxTreatment").GetString(), investments[normal].GetProperty("taxTreatment").GetString()));

        // Paying the owner: the money leaves business cash, and still sits in the fund, in a personal account
        await client.RecordAsync("transfer", new { fromAccountId = gold, fromFundId = OperatingCapital, toAccountId = aspire, toFundId = OwnersDistribution, amount = 1_000m, date = Day(-5), description = "Owner's distribution" });
        balances = await Ok(client.GetAsync("/api/balances"));
        Assert.Equal(14_000m, Assert.Single(balances.GetProperty("cashTotals").EnumerateArray()).GetProperty("amount").GetDecimal());
        Assert.Contains(("Personal", "ZAR", 3_000m), OtherCash(balances));

        // The period agrees with the headline, and shows the distribution as money out
        var period = Assert.Single((await Ok(client.GetAsync($"/api/overview?from={Day(-10)}&to={Day(0)}"))).GetProperty("currencies").EnumerateArray());
        var transfers = period.GetProperty("movements").EnumerateArray().Single(m => m.GetProperty("type").GetString() == "Transfer");
        Assert.Equal((15_000m, -1_000m, -1_000m, 14_000m), (
            period.GetProperty("cashAtStart").GetDecimal(), transfers.GetProperty("amount").GetDecimal(),
            period.GetProperty("netCashMovement").GetDecimal(), period.GetProperty("cashAtEnd").GetDecimal()));

        // The ledger's cash filter is the same scope: an entry only in a personal account is not in it
        var aspireExpense = await client.RecordAsync("expense", new { accountId = aspire, fundId = OwnersDistribution, amount = 10m, date = Day(-1), description = "Personal spend" });
        var cashList = (await Ok(client.GetAsync($"/api/transactions?from={Day(-2)}&to={Day(0)}&cash=true"))).GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetInt32());
        Assert.DoesNotContain(aspireExpense.GetProperty("id").GetInt32(), cashList);

        // Stating the owner moves the money into the headline; the audit log keeps the change
        var named = await Ok(client.PutAsJsonAsync($"/api/financial-accounts/{unstated}", new { name = "Unstated Bank", ownership = "Company", category = "Savings", taxTreatment = "Taxable", reference = "9876" }));
        Assert.Equal(("Savings", "9876"), (named.GetProperty("category").GetString(), named.GetProperty("reference").GetString()));
        Assert.Equal(14_700m, Assert.Single((await Ok(client.GetAsync("/api/balances"))).GetProperty("cashTotals").EnumerateArray()).GetProperty("amount").GetDecimal());
        var audit = (await Ok(client.GetAsync("/api/audit-events?pageSize=100"))).GetProperty("items").EnumerateArray()
            .First(e => e.GetProperty("action").GetString() == "account.updated" && e.GetProperty("entityId").GetString() == unstated.ToString());
        Assert.Contains("\"ownership\":\"Unspecified\"", audit.GetProperty("data").GetString());
        Assert.Contains("9876", audit.GetProperty("data").GetString());
    }

    [Fact]
    public async Task Continuity_and_purchases_count_company_money_only()
    {
        var client = await Owner();
        var company = await Account(client, "Reserve Company", "Bank", "Company", "Savings");
        var personal = await Account(client, "Reserve Personal", "Bank", "Personal", "Savings");
        await Opening(client, company, Continuity, 4_000m);
        await Opening(client, personal, Continuity, 6_000m);

        var report = await Ok(client.GetAsync("/api/continuity"));
        Assert.Equal(report.GetProperty("cash").GetProperty("amount").GetDecimal(), report.GetProperty("holdings").EnumerateArray().Sum(h => h.GetProperty("amount").GetProperty("amount").GetDecimal()));
        Assert.DoesNotContain(report.GetProperty("holdings").EnumerateArray(), h => h.GetProperty("accountName").GetString() == "Reserve Personal");
        var notCounted = report.GetProperty("notCounted").EnumerateArray().Single(n => n.GetProperty("accountName").GetString() == "Reserve Personal");
        Assert.Equal(("In a personal account", 6_000m), (notCounted.GetProperty("reason").GetString(), notCounted.GetProperty("amount").GetProperty("amount").GetDecimal()));

        var purchase = await Ok(client.PostAsJsonAsync("/api/planned-purchases", new { name = "From the reserve", amount = 5_000m, currency = "ZAR", fundId = Continuity }));
        var affordability = purchase.GetProperty("affordability");
        Assert.DoesNotContain(affordability.GetProperty("available").GetProperty("amount").GetDecimal(), new[] { 6_000m, 10_000m });
    }

    [Fact]
    public async Task What_an_account_is_has_to_make_sense()
    {
        var client = await Owner();
        object Request(string name, string type = "Bank", string category = "NotStated", string? reference = null, string? openedOn = null, int? homeFundId = null) =>
            new { name, type, currency = "ZAR", ownership = "Company", category, reference, openedOn, homeFundId };
        async Task<HttpStatusCode> Post(object body) => (await client.PostAsJsonAsync("/api/financial-accounts", body)).StatusCode;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(Request("Full number", reference: "62812345678")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(Request("Wrong category", "Brokerage", "Savings")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(Request("Wrong bank", "Bank", "Investment")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(Request("Future", openedOn: Day(5))));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(Request("No fund", homeFundId: 999_999)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(Request("System fund", homeFundId: Unallocated)));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(Request("Too long", reference: new string('x', 21))));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { name = "Bad enum", type = "Bank", currency = "ZAR", ownership = "Company", category = "Nonsense" }));

        // None was saved, and what was omitted is simply not stated
        var plain = await Ok(client.PostAsJsonAsync("/api/financial-accounts", new { name = "Plain", type = "Bank", currency = "ZAR", ownership = "Company" }));
        Assert.Equal(("NotStated", "NotStated"), (plain.GetProperty("category").GetString(), plain.GetProperty("taxTreatment").GetString()));
        Assert.DoesNotContain((await Ok(client.GetAsync("/api/financial-accounts"))).EnumerateArray(), a => a.GetProperty("name").GetString() == "Full number");

        // An update is held to the same rules, and keeps the type and currency
        var id = plain.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/financial-accounts/{id}", new { name = "Plain", ownership = "Company", category = "Investment" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/financial-accounts/{id}", new { name = "Plain", ownership = "Company", reference = "62812345678" })).StatusCode);
        Assert.Equal("Bank", (await Ok(client.GetAsync($"/api/financial-accounts/{id}"))).GetProperty("type").GetString());
    }
}
