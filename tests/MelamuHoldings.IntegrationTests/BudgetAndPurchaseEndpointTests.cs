using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// Phase 7.4: budget versus actual from expense categories
public class BudgetEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public BudgetEndpointTests(ApiFactory factory) => _factory = factory;

    private static readonly DateOnly ThisMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    private static readonly DateOnly LastMonth = ThisMonth.AddMonths(-1);
    private static string Month(DateOnly month) => month.ToString("yyyy-MM");
    private static string D(DateOnly month, int day) => month.AddDays(day - 1).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Actual_is_the_months_unreversed_expenses_by_category_and_currency_and_the_rest_is_shown_as_unbudgeted()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var zar = await client.CreateAccountAsync("Budget ZAR", "ZAR");
        var usd = await client.CreateAccountAsync("Budget USD", "USD");
        await client.RecordAsync("opening-balance", new { accountId = zar, fundId = OperatingCapital, amount = 50000m, date = D(LastMonth.AddMonths(-1), 1), description = "Carried over" });
        await client.RecordAsync("opening-balance", new { accountId = usd, fundId = OperatingCapital, amount = 500m, date = D(LastMonth.AddMonths(-1), 1), description = "Carried over" });

        Task Spend(int account, decimal amount, string? category, DateOnly month, int day = 5) =>
            client.RecordAsync("expense", new { accountId = account, fundId = OperatingCapital, amount, category, date = D(month, day), description = "Spent" });
        async Task Budget(string category, decimal amount, string currency) =>
            await Ok(client.PostAsJsonAsync("/api/budgets", new { month = Month(LastMonth), category, amount, currency }));

        await Budget("Rent", 8000m, "ZAR");
        await Budget("Software", 1000m, "ZAR");
        await Budget("Software", 50m, "USD");

        await Spend(zar, 8500m, "rent", LastMonth);              // another case, same category; over budget
        await Spend(zar, 300m, " Software ", LastMonth);
        var reversed = await client.RecordAsync("expense", new { accountId = zar, fundId = OperatingCapital, amount = 200m, category = "Software", date = D(LastMonth, 6), description = "Wrong" });
        await Ok(client.PostAsync($"/api/transactions/{reversed.GetProperty("id").GetInt32()}/reverse", new { date = Today, reason = "Wrong" }, Guid.NewGuid().ToString("N")));
        await Spend(usd, 20m, "Software", LastMonth);
        await Spend(usd, 10m, "Rent", LastMonth);                // a budget in rand does not cover dollars
        await Spend(zar, 150m, "Travel", LastMonth);
        await Spend(zar, 75m, null, LastMonth);
        await Spend(zar, 999m, "Rent", ThisMonth, 1);            // another month
        await Spend(zar, 111m, "Rent", LastMonth.AddMonths(-1), 20);

        var report = await Ok(client.GetAsync($"/api/budgets?month={Month(LastMonth)}"));
        Assert.Equal((Month(LastMonth), D(LastMonth, 1)), (report.GetProperty("month").GetString(), report.GetProperty("from").GetString()));

        var lines = report.GetProperty("lines").EnumerateArray().Select(l => (
            l.GetProperty("category").GetString(), l.GetProperty("budget").GetProperty("currency").GetString(), l.GetProperty("actual").GetProperty("amount").GetDecimal(),
            l.GetProperty("remaining").GetProperty("amount").GetDecimal(), l.GetProperty("usedPercent").GetDecimal(), l.GetProperty("exceeded").GetBoolean(), l.GetProperty("transactions").GetInt32())).ToList();
        Assert.Equal([("Rent", "ZAR", 8500m, -500m, 106.2m, true, 1), ("Software", "USD", 20m, 30m, 40.0m, false, 1), ("Software", "ZAR", 300m, 700m, 30.0m, false, 1)], lines);

        var unbudgeted = report.GetProperty("unbudgeted").EnumerateArray().Select(u => (
            u.GetProperty("category").ValueKind == JsonValueKind.Null ? null : u.GetProperty("category").GetString(), u.GetProperty("actual").GetProperty("amount").GetDecimal(), u.GetProperty("actual").GetProperty("currency").GetString())).ToList();
        Assert.Equal([("Rent", 10m, "USD"), ("Travel", 150m, "ZAR"), (null, 75m, "ZAR")], unbudgeted);

        var totals = report.GetProperty("totals").EnumerateArray().Select(t => (
            t.GetProperty("currency").GetString(), t.GetProperty("budget").GetDecimal(), t.GetProperty("actualBudgeted").GetDecimal(), t.GetProperty("actualUnbudgeted").GetDecimal())).ToList();
        Assert.Equal([("USD", 50m, 20m, 10m), ("ZAR", 9000m, 8800m, 225m)], totals);

        // No month given: this month, with no budget, and the spending shown as unbudgeted
        var current = await Ok(client.GetAsync("/api/budgets"));
        Assert.Equal(Month(ThisMonth), current.GetProperty("month").GetString());
        Assert.Empty(current.GetProperty("lines").EnumerateArray());
        Assert.Equal(999m, current.GetProperty("unbudgeted")[0].GetProperty("actual").GetProperty("amount").GetDecimal());
    }
}

public class BudgetRuleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public BudgetRuleTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Budget_lines_are_validated_unique_and_audited()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        async Task<HttpStatusCode> Post(object body) => (await client.PostAsJsonAsync("/api/budgets", body)).StatusCode;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(new { month = "2026-13", category = "Rent", amount = 1m, currency = "ZAR" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(new { month = "October", category = "Rent", amount = 1m, currency = "ZAR" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(new { month = "2026-10", category = "Rent", amount = 0m, currency = "ZAR" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(new { month = "2026-10", category = "Rent", amount = 10.005m, currency = "ZAR" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Post(new { month = "2026-10", category = "Rent", amount = 1m, currency = "XXX" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(new { month = "2026-10", amount = 1m, currency = "ZAR" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync("/api/budgets?month=nonsense")).StatusCode);

        var created = await client.PostAsJsonAsync("/api/budgets", new { month = "2026-10", category = "Rent", amount = 8000m, currency = "ZAR" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, await Post(new { month = "2026-10", category = "RENT", amount = 1m, currency = "ZAR" }));
        Assert.Equal(HttpStatusCode.Created, await Post(new { month = "2026-10", category = "Rent", amount = 1m, currency = "USD" }));      // another currency
        Assert.Equal(HttpStatusCode.Created, await Post(new { month = "2026-11", category = "Rent", amount = 1m, currency = "ZAR" }));      // another month
        Assert.Equal(HttpStatusCode.Created, await Post(new { month = "2026-10", category = "Fibre", amount = 1m, currency = "ZAR" }));

        var updated = await client.PutAsJsonAsync($"/api/budgets/{id}", new { category = "Rent", amount = 9000m });
        Assert.Equal(9000m, (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("budget").GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/budgets/{id}", new { category = "Fibre", amount = 9000m })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/budgets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/budgets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/budgets/{id}", new { category = "Rent", amount = 1m })).StatusCode);

        var audit = (await (await client.GetAsync("/api/audit-events?pageSize=100")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("budget.created", audit);
        Assert.Contains("budget.updated", audit);
        Assert.Contains("budget.deleted", audit);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/budgets")).StatusCode);
    }
}

// Phase 7.4: planned purchases and whether they can be afforded
public class PlannedPurchaseEndpointTests : IClassFixture<ApiFactory>
{
    private const int WealthFund = 4, OwnersDistribution = 5;
    private readonly ApiFactory _factory;

    public PlannedPurchaseEndpointTests(ApiFactory factory) => _factory = factory;

    private static decimal Money(JsonElement e, string property) => e.GetProperty(property).GetProperty("amount").GetDecimal();
    private static string Verdict(JsonElement purchase) => purchase.GetProperty("affordability").GetProperty("verdict").GetString()!;

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Whether_it_can_be_afforded_comes_from_bank_and_cash_balances_committed_purchases_and_the_minimum()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var bank = await client.CreateAccountAsync("Purchase bank", "ZAR");
        var broker = await client.CreateAccountAsync("Purchase broker", "ZAR", "Brokerage");
        await client.RecordAsync("opening-balance", new { accountId = bank, fundId = OperatingCapital, amount = 20000m, date = Today, description = "Carried over" });
        await client.RecordAsync("opening-balance", new { accountId = broker, fundId = OperatingCapital, amount = 5000m, date = Today, description = "Not spendable" });
        await client.RecordAsync("opening-balance", new { accountId = bank, fundId = WealthFund, amount = 1000m, date = Today, description = "Wealth" });
        await Ok(client.PutAsJsonAsync($"/api/funds/{OperatingCapital}", new { name = "Operating Capital", thresholdCurrency = "ZAR", minimum = 5000m }));
        await Ok(client.PutAsJsonAsync($"/api/funds/{OwnersDistribution}", new { name = "Owner's Distribution", thresholdCurrency = "USD", minimum = 100m }));

        async Task<JsonElement> Propose(string name, decimal amount, int fundId = OperatingCapital) =>
            await Ok(client.PostAsJsonAsync("/api/planned-purchases", new { name, amount, currency = "ZAR", fundId, category = "Equipment" }));
        Task<JsonElement> Get(JsonElement p) => Ok(client.GetAsync($"/api/planned-purchases/{p.GetProperty("id").GetInt32()}"));

        // Within the minimum: the brokerage money is not counted
        var laptop = await Propose("Laptop", 10000m);
        var check = laptop.GetProperty("affordability");
        Assert.Equal("Affordable", Verdict(laptop));
        Assert.Equal((20000m, 0m, 10000m, 10000m, 5000m), (Money(check, "available"), Money(check, "committedElsewhere"), Money(check, "purchase"), Money(check, "after"), Money(check, "minimum")));
        Assert.Equal(JsonValueKind.Null, check.GetProperty("shortfall").ValueKind);

        // Exactly at the minimum is allowed; a cent below is not
        Assert.Equal("Affordable", Verdict(await Propose("Exactly", 15000m)));
        var server = await Propose("Server", 15000.01m);
        Assert.Equal(("BreachesMinimum", 0.01m), (Verdict(server), Money(server.GetProperty("affordability"), "shortfall")));

        // A commitment reserves its money from the others, and not from itself
        await Ok(client.PostAsync($"/api/planned-purchases/{laptop.GetProperty("id").GetInt32()}/commit", null));
        check = (await Get(server)).GetProperty("affordability");
        Assert.Equal((10000m, -5000.01m, 10000.01m), (Money(check, "committedElsewhere"), Money(check, "after"), Money(check, "shortfall")));
        Assert.Equal(0m, Money((await Get(laptop)).GetProperty("affordability"), "committedElsewhere"));

        // Committing is allowed even when it breaches: the check warns and the decision is the owner's
        var committed = await Ok(client.PostAsync($"/api/planned-purchases/{server.GetProperty("id").GetInt32()}/commit", null));
        Assert.Equal(("Committed", "BreachesMinimum"), (committed.GetProperty("status").GetString(), Verdict(committed)));
        var withdrawn = await Ok(client.PostAsync($"/api/planned-purchases/{server.GetProperty("id").GetInt32()}/withdraw-commitment", null));
        Assert.Equal("Proposed", withdrawn.GetProperty("status").GetString());

        // No minimum: only whether the money is there
        Assert.Equal("NoMinimum", Verdict(await Propose("Desk", 500m, WealthFund)));
        var chair = await Propose("Chair", 1500m, WealthFund);
        Assert.Equal(("NotEnoughMoney", 500m), (Verdict(chair), Money(chair.GetProperty("affordability"), "shortfall")));

        // A minimum in another currency is shown and never converted
        var other = await Propose("Gift", 10m, OwnersDistribution);
        Assert.Equal("NotEnoughMoney", Verdict(other));
        Assert.Equal(JsonValueKind.Null, other.GetProperty("affordability").GetProperty("minimum").ValueKind);
        Assert.Equal(100m, other.GetProperty("affordability").GetProperty("minimumInOtherCurrency").GetProperty("amount").GetDecimal());

        // Obligations due soon are shown beside the answer and not deducted; other currencies and later dates are left out
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task Oblige(string name, decimal amount, string currency, DateOnly due) => await Ok(client.PostAsJsonAsync("/api/obligations",
            new { name, amount, currency, recurrence = "None", firstDueDate = due.ToString("yyyy-MM-dd") }));
        await Oblige("Rent soon", 1000m, "ZAR", today);
        await Oblige("Dollars soon", 7m, "USD", today);
        await Oblige("Rent later", 3000m, "ZAR", today.AddDays(31));
        check = (await Get(laptop)).GetProperty("affordability");
        Assert.Equal((1000m, 10000m), (Money(check, "obligationsDue"), Money(check, "after")));
    }
}

public class PlannedPurchaseLifecycleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PlannedPurchaseLifecycleTests(ApiFactory factory) => _factory = factory;

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_purchase_links_an_expense_never_creates_one_and_each_expense_pays_for_one_thing()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var bank = await client.CreateAccountAsync("Lifecycle bank", "ZAR");
        var usd = await client.CreateAccountAsync("Lifecycle usd", "USD");
        await client.RecordAsync("opening-balance", new { accountId = bank, fundId = OperatingCapital, amount = 30000m, date = Today, description = "Carried over" });
        await client.RecordAsync("opening-balance", new { accountId = usd, fundId = OperatingCapital, amount = 300m, date = Today, description = "Carried over" });

        async Task<int> Spend(int account, decimal amount, string description) =>
            (await client.RecordAsync("expense", new { accountId = account, fundId = OperatingCapital, amount, date = Today, description, category = "Equipment" })).GetProperty("id").GetInt32();
        async Task<int> Propose(string name, decimal amount = 10000m, int fundId = OperatingCapital) =>
            (await Ok(client.PostAsJsonAsync("/api/planned-purchases", new { name, amount, currency = "ZAR", fundId }))).GetProperty("id").GetInt32();
        async Task<HttpStatusCode> Status(string url, object? body = null) => (await client.PostAsJsonAsync(url, body ?? new { })).StatusCode;
        async Task<List<int>> Candidates(int id) =>
            (await Ok(client.GetAsync($"/api/planned-purchases/{id}/expense-candidates"))).EnumerateArray().Select(c => c.GetProperty("transactionId").GetInt32()).ToList();

        var laptop = await Propose("Laptop");
        var transactionsBefore = (await Ok(client.GetAsync("/api/transactions?pageSize=100"))).GetProperty("totalCount").GetInt32();

        var bought = await Spend(bank, 9800m, "Laptop from the shop");
        await Spend(usd, 50m, "Dollar expense");                                           // another currency
        var wrong = await Spend(bank, 123m, "Wrong");
        await Ok(client.PostAsync($"/api/transactions/{wrong}/reverse", new { date = Today, reason = "Wrong" }, Guid.NewGuid().ToString("N")));
        var rent = await Spend(bank, 700m, "Rent");
        var obligation = (await Ok(client.PostAsJsonAsync("/api/obligations", new { name = "Rent", amount = 700m, currency = "ZAR", recurrence = "Monthly", firstDueDate = Today }))).GetProperty("id").GetInt32();
        await Ok(client.PostAsJsonAsync($"/api/obligations/{obligation}/payments", new { transactionId = rent }));
        Assert.Equal([bought], await Candidates(laptop));

        // Not before it is committed, and not with an expense that already paid an obligation
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status($"/api/planned-purchases/{laptop}/purchase", new { transactionId = bought }));
        await Ok(client.PostAsync($"/api/planned-purchases/{laptop}/commit", null));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status($"/api/planned-purchases/{laptop}/purchase", new { transactionId = rent }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status($"/api/planned-purchases/{laptop}/purchase", new { transactionId = wrong }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status($"/api/planned-purchases/{laptop}/purchase", new { transactionId = 999999 }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/planned-purchases/{laptop}", new { name = "Laptop", amount = 1m, currency = "ZAR", fundId = OperatingCapital })).StatusCode);

        // Bought: what it cost is recorded, no new transaction appears, and the check no longer applies
        var transactionsAfterSpending = (await Ok(client.GetAsync("/api/transactions?pageSize=100"))).GetProperty("totalCount").GetInt32();
        var purchased = await Ok(client.PostAsJsonAsync($"/api/planned-purchases/{laptop}/purchase", new { transactionId = bought }));
        Assert.Equal(("Purchased", bought, 9800m, 10000m), (purchased.GetProperty("status").GetString(), purchased.GetProperty("transactionId").GetInt32(),
            purchased.GetProperty("paid").GetProperty("amount").GetDecimal(), purchased.GetProperty("amount").GetProperty("amount").GetDecimal()));
        Assert.Equal(JsonValueKind.Null, purchased.GetProperty("affordability").ValueKind);
        Assert.Equal(transactionsAfterSpending, (await Ok(client.GetAsync("/api/transactions?pageSize=100"))).GetProperty("totalCount").GetInt32());
        Assert.True(transactionsAfterSpending > transactionsBefore);

        // Closed ones are listed only when asked for
        Assert.Empty((await Ok(client.GetAsync("/api/planned-purchases"))).EnumerateArray());
        Assert.Single((await Ok(client.GetAsync("/api/planned-purchases?includeClosed=true"))).EnumerateArray());

        // The expense cannot pay for a second purchase or an obligation
        var second = await Propose("Second laptop");
        await Ok(client.PostAsync($"/api/planned-purchases/{second}/commit", null));
        Assert.Empty(await Candidates(second));
        Assert.Equal(HttpStatusCode.Conflict, await Status($"/api/planned-purchases/{second}/purchase", new { transactionId = bought }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status($"/api/obligations/{obligation}/payments", new { transactionId = bought }));
        Assert.DoesNotContain((await Ok(client.GetAsync($"/api/obligations/{obligation}/payment-candidates"))).EnumerateArray(), c => c.GetProperty("transactionId").GetInt32() == bought);

        // A later reversal is flagged, and nothing is changed automatically
        await Ok(client.PostAsync($"/api/transactions/{bought}/reverse", new { date = Today, reason = "Returned" }, Guid.NewGuid().ToString("N")));
        var flagged = await Ok(client.GetAsync($"/api/planned-purchases/{laptop}"));
        Assert.Equal(("Purchased", true), (flagged.GetProperty("status").GetString(), flagged.GetProperty("expenseReversed").GetBoolean()));

        // Undo returns it to committed; the expense stays in the ledger
        var undone = await Ok(client.PostAsync($"/api/planned-purchases/{laptop}/purchase/undo", null));
        Assert.Equal(("Committed", JsonValueKind.Null), (undone.GetProperty("status").GetString(), undone.GetProperty("transactionId").ValueKind));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status($"/api/planned-purchases/{laptop}/purchase", new { transactionId = bought }));   // now reversed
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/transactions/{bought}")).StatusCode);

        // Names are unique among open ones; cancelling frees the name
        Assert.Equal(HttpStatusCode.Conflict, await Status("/api/planned-purchases", new { name = "Laptop", amount = 1m, currency = "ZAR", fundId = OperatingCapital }));
        await Ok(client.PostAsync($"/api/planned-purchases/{laptop}/cancel", null));
        await Propose("Laptop");

        // Refusals
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status("/api/planned-purchases", new { name = "A", amount = 1m, currency = "ZAR", fundId = Unallocated }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status("/api/planned-purchases", new { name = "A", amount = 0m, currency = "ZAR", fundId = OperatingCapital }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status("/api/planned-purchases", new { name = "A", amount = 10.005m, currency = "ZAR", fundId = OperatingCapital }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, await Status("/api/planned-purchases", new { name = "A", amount = 1m, currency = "ZAR", fundId = 999999 }));
        Assert.Equal(HttpStatusCode.BadRequest, await Status("/api/planned-purchases", new { name = "A", amount = 1m, currency = "ZAR" }));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/planned-purchases/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/planned-purchases")).StatusCode);

        var audit = (await Ok(client.GetAsync("/api/audit-events?pageSize=100"))).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        foreach (var action in new[] { "purchase.proposed", "purchase.committed", "purchase.bought", "purchase.undone", "purchase.cancelled" })
            Assert.Contains(action, audit);
    }
}
