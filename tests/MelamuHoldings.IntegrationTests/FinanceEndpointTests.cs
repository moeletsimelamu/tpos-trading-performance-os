using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// A fresh database of its own, so cash and fund totals can be asserted exactly
public class LedgerFlowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public LedgerFlowTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Balances_reconcile_across_payout_exchange_earmark_transfer_and_expense()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var payouts = await client.CreateAccountAsync("Payouts USD", "USD");
        var operating = await client.CreateAccountAsync("Operating ZAR", "ZAR");
        var reserve = await client.CreateAccountAsync("Reserve ZAR", "ZAR");

        await client.RecordAsync("opening-balance", new { accountId = operating, fundId = Unallocated, amount = 1000m, date = Today, description = "Balance carried over" });
        await client.RecordAsync("income", new { accountId = payouts, fundId = Unallocated, amount = 1350m, date = Today, description = "Prop firm payout", category = "Trading payout" });
        var exchange = await client.RecordAsync("exchange", new { fromAccountId = payouts, fromFundId = Unallocated, amountSent = 1000m, toAccountId = operating, toFundId = Unallocated, amountReceived = 18250.50m, date = Today, description = "Withdraw to ZAR" });
        await client.RecordAsync("reallocation", new { accountId = operating, fromFundId = Unallocated, toFundId = Continuity, amount = 5000m, date = Today, description = "Earmark continuity" });
        await client.RecordAsync("transfer", new { fromAccountId = operating, fromFundId = Continuity, toAccountId = reserve, toFundId = Continuity, amount = 2000m, date = Today, description = "Move reserve to its own account" });
        await client.RecordAsync("expense", new { accountId = operating, fundId = Unallocated, amount = 300m, date = Today, description = "Data feed", category = "Software" });

        Assert.Equal(18.2505m, exchange.GetProperty("exchangeRate").GetDecimal());

        var usd = await client.AccountAsync(payouts);
        var zar = await client.AccountAsync(operating);
        var res = await client.AccountAsync(reserve);
        Assert.Equal(350m, usd.Balance());
        Assert.Equal(16950.50m, zar.Balance());                      // 1000 + 18250.50 - 2000 - 300
        Assert.Equal(13950.50m, zar.FundBalance(Unallocated));       // 1000 + 18250.50 - 5000 - 300
        Assert.Equal(3000m, zar.FundBalance(Continuity));            // earmarked 5000, moved 2000 out
        Assert.Equal(2000m, res.FundBalance(Continuity));

        var balances = await (await client.GetAsync("/api/balances")).JsonAsync();
        var cash = balances.GetProperty("cashTotals").EnumerateArray().ToDictionary(m => m.GetProperty("currency").GetString()!, m => m.GetProperty("amount").GetDecimal());
        Assert.Equal(350m, cash["USD"]);
        Assert.Equal(18950.50m, cash["ZAR"]);
        var continuity = balances.GetProperty("funds").EnumerateArray().Single(f => f.GetProperty("id").GetInt32() == Continuity);
        Assert.Equal(5000m, continuity.GetProperty("balances").EnumerateArray().Single().GetProperty("amount").GetDecimal());
    }
}

public class LedgerRuleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public LedgerRuleTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    [Fact]
    public async Task Default_funds_are_seeded_with_unallocated_as_the_system_fund()
    {
        var funds = await (await (await Owner()).GetAsync("/api/funds")).JsonAsync();

        var names = funds.EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Unallocated", "Operating Capital", "Business Continuity", "Wealth Fund", "Owner's Distribution"], names);
        Assert.True(funds[0].GetProperty("isSystem").GetBoolean());
    }

    [Fact]
    public async Task Accounts_validate_currency_reject_duplicate_names_and_report_enums_by_name()
    {
        var client = await Owner();
        var name = Unique("Ops");

        var created = await client.PostAsJsonAsync("/api/financial-accounts", new { name, type = "Bank", currency = "zar", ownership = "Company" });
        var duplicate = await client.PostAsJsonAsync("/api/financial-accounts", new { name, type = "Cash", currency = "ZAR", ownership = "Company" });
        var badCurrency = await client.PostAsJsonAsync("/api/financial-accounts", new { name = Unique("X"), type = "Bank", currency = "XYZ", ownership = "Company" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = await created.JsonAsync();
        Assert.Equal("Bank", account.GetProperty("type").GetString());
        Assert.Equal("ZAR", account.GetProperty("currency").GetString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badCurrency.StatusCode);
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_posts_once()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Idem"), "ZAR");
        var body = new { accountId = account, fundId = Unallocated, amount = 500m, date = Today, description = "Payout" };

        var first = await client.PostAsync("/api/transactions/income", body, "retry-key-1-" + account);
        var retry = await client.PostAsync("/api/transactions/income", body, "retry-key-1-" + account);
        var otherPayload = await client.PostAsync("/api/transactions/income", new { accountId = account, fundId = Unallocated, amount = 999m, date = Today, description = "Payout" }, "retry-key-1-" + account);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal((await first.JsonAsync()).GetProperty("id").GetInt32(), (await retry.JsonAsync()).GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, otherPayload.StatusCode);
        Assert.Equal(500m, (await client.AccountAsync(account)).Balance());
    }

    [Fact]
    public async Task Concurrent_retries_with_one_key_post_exactly_once()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Race"), "ZAR");
        var body = new { accountId = account, fundId = Unallocated, amount = 250m, date = Today, description = "Double click" };
        var key = "race-" + account;

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsync("/api/transactions/income", body, key)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, $"Unexpected {(int)r.StatusCode}"));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.JsonAsync()).GetProperty("id").GetInt32()));
        Assert.Single(ids.Distinct());
        Assert.Equal(250m, (await client.AccountAsync(account)).Balance());
    }

    [Fact]
    public async Task Reversal_cancels_a_transaction_exactly_once()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Rev"), "ZAR");
        var income = await client.RecordAsync("income", new { accountId = account, fundId = Unallocated, amount = 75.25m, date = Today, description = "Wrong account" });
        var id = income.GetProperty("id").GetInt32();

        var reversal = await client.PostAsync($"/api/transactions/{id}/reverse", new { date = Today, reason = "Posted to the wrong account" }, Guid.NewGuid().ToString("N"));
        var again = await client.PostAsync($"/api/transactions/{id}/reverse", new { date = Today, reason = "Again" }, Guid.NewGuid().ToString("N"));
        var reversalId = (await reversal.JsonAsync()).GetProperty("id").GetInt32();
        var reverseTheReversal = await client.PostAsync($"/api/transactions/{reversalId}/reverse", new { date = Today, reason = "Undo" }, Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Created, reversal.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reverseTheReversal.StatusCode);
        Assert.Equal(0m, (await client.AccountAsync(account)).Balance());
        var original = await (await client.GetAsync($"/api/transactions/{id}")).JsonAsync();
        Assert.Equal(reversalId, original.GetProperty("reversedByTransactionId").GetInt32());
    }

    [Fact]
    public async Task Malformed_requests_are_400_and_broken_rules_are_422()
    {
        var client = await Owner();
        var zar = await client.CreateAccountAsync(Unique("Rules ZAR"), "ZAR");
        var usd = await client.CreateAccountAsync(Unique("Rules USD"), "USD");

        var missingAmount = await client.PostAsync("/api/transactions/income", new { accountId = zar, fundId = Unallocated, date = Today, description = "x" }, null);
        var badKey = await client.PostAsync("/api/transactions/income", new { accountId = zar, fundId = Unallocated, amount = 1m, date = Today, description = "x" }, "not a valid key!");
        var subCent = await client.PostAsync("/api/transactions/income", new { accountId = zar, fundId = Unallocated, amount = 10.005m, date = Today, description = "x" }, null);
        var zero = await client.PostAsync("/api/transactions/expense", new { accountId = zar, fundId = Unallocated, amount = 0m, date = Today, description = "x" }, null);
        var crossCurrency = await client.PostAsync("/api/transactions/transfer", new { fromAccountId = usd, fromFundId = Unallocated, toAccountId = zar, toFundId = Unallocated, amount = 5m, date = Today, description = "x" }, null);
        var unknownAccount = await client.PostAsync("/api/transactions/income", new { accountId = 999999, fundId = Unallocated, amount = 1m, date = Today, description = "x" }, null);
        var future = await client.PostAsync("/api/transactions/income", new { accountId = zar, fundId = Unallocated, amount = 1m, date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5).ToString("yyyy-MM-dd"), description = "x" }, null);

        Assert.Equal(HttpStatusCode.BadRequest, missingAmount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
        foreach (var response in new[] { subCent, zero, crossCurrency, unknownAccount, future })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        Assert.Contains("currency exchange", (await crossCurrency.JsonAsync()).GetProperty("title").GetString());
        Assert.Equal(0m, (await client.AccountAsync(zar)).Balance());
    }

    [Fact]
    public async Task One_opening_balance_per_account_and_fund_until_it_is_reversed()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Opening"), "ZAR");
        var body = new { accountId = account, fundId = Unallocated, amount = 100m, date = Today, description = "Opening" };

        var first = await client.RecordAsync("opening-balance", body);
        var second = await client.PostAsync("/api/transactions/opening-balance", body, Guid.NewGuid().ToString("N"));
        await client.PostAsync($"/api/transactions/{first.GetProperty("id").GetInt32()}/reverse", new { date = Today, reason = "Wrong figure" }, Guid.NewGuid().ToString("N"));
        var replacement = await client.PostAsync("/api/transactions/opening-balance", body with { }, Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        Assert.Equal(100m, (await client.AccountAsync(account)).Balance());
    }

    [Fact]
    public async Task Accounts_close_only_when_empty_and_then_accept_no_entries()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Close"), "ZAR");
        await client.RecordAsync("income", new { accountId = account, fundId = Unallocated, amount = 10m, date = Today, description = "In" });

        var notEmpty = await client.PostAsync($"/api/financial-accounts/{account}/close", new { }, null);
        await client.RecordAsync("expense", new { accountId = account, fundId = Unallocated, amount = 10m, date = Today, description = "Out" });
        var closed = await client.PostAsync($"/api/financial-accounts/{account}/close", new { }, null);
        var afterClose = await client.PostAsync("/api/transactions/income", new { accountId = account, fundId = Unallocated, amount = 1m, date = Today, description = "Late" }, null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, notEmpty.StatusCode);
        Assert.Equal("Closed", (await closed.JsonAsync()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, afterClose.StatusCode);
    }

    [Fact]
    public async Task Transactions_filter_by_account_and_type_and_page()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("List"), "ZAR");
        for (var i = 1; i <= 3; i++)
            await client.RecordAsync("income", new { accountId = account, fundId = Unallocated, amount = i * 10m, date = Today, description = $"Income {i}" });
        await client.RecordAsync("expense", new { accountId = account, fundId = Unallocated, amount = 5m, date = Today, description = "Expense" });

        var page1 = await (await client.GetAsync($"/api/transactions?accountId={account}&type=Income&pageSize=2&page=1")).JsonAsync();
        var page2 = await (await client.GetAsync($"/api/transactions?accountId={account}&type=Income&pageSize=2&page=2")).JsonAsync();
        var badPage = await client.GetAsync("/api/transactions?pageSize=500");

        Assert.Equal(3, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());
        Assert.All(page1.GetProperty("items").EnumerateArray(), t => Assert.Equal("Income", t.GetProperty("type").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
    }

    [Fact]
    public async Task Balances_as_of_a_date_exclude_later_transactions()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("AsOf"), "ZAR");
        var lastWeek = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        await client.RecordAsync("income", new { accountId = account, fundId = Unallocated, amount = 100m, date = lastWeek.ToString("yyyy-MM-dd"), description = "Earlier" });
        await client.RecordAsync("income", new { accountId = account, fundId = Unallocated, amount = 40m, date = Today, description = "Later" });

        var asOf = await (await client.GetAsync($"/api/balances?asOf={lastWeek.AddDays(1):yyyy-MM-dd}")).JsonAsync();
        var now = await (await client.GetAsync("/api/balances")).JsonAsync();

        decimal BalanceIn(JsonElement balances) => balances.GetProperty("accounts").EnumerateArray()
            .Single(a => a.GetProperty("id").GetInt32() == account).Balance();
        Assert.Equal(100m, BalanceIn(asOf));
        Assert.Equal(140m, BalanceIn(now));
    }

    [Fact]
    public async Task Every_finance_change_is_written_to_the_audit_log()
    {
        var client = await Owner();
        var name = Unique("Audited");
        var account = await client.CreateAccountAsync(name, "ZAR");
        var income = await client.RecordAsync("income", new { accountId = account, fundId = Unallocated, amount = 12.34m, date = Today, description = "Audited income" });

        var events = (await (await client.GetAsync("/api/audit-events?pageSize=100")).JsonAsync()).GetProperty("items").EnumerateArray().ToList();

        Assert.Contains(events, e => e.GetProperty("action").GetString() == "account.created" && e.GetProperty("entityId").GetString() == account.ToString());
        var recorded = Assert.Single(events, e => e.GetProperty("action").GetString() == "transaction.recorded"
            && e.GetProperty("entityId").GetString() == income.GetProperty("id").GetInt32().ToString());
        Assert.Contains("ZAR 12.34", recorded.GetProperty("summary").GetString());
    }
}

public class FinanceAccessTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public FinanceAccessTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/funds")]
    [InlineData("/api/financial-accounts")]
    [InlineData("/api/transactions")]
    [InlineData("/api/balances")]
    [InlineData("/api/audit-events")]
    [InlineData("/api/payouts")]
    [InlineData("/api/allocations")]
    [InlineData("/api/allocation-policies")]
    public async Task Finance_endpoints_reject_anonymous_requests(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task A_valid_token_without_the_owner_role_cannot_reach_finance()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenWithoutOwnerRole());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/balances")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/payouts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/allocations/preview", new { payoutId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/trades")).StatusCode);
    }

    // Signed with the test key and issuer, but carrying no role claim
    private static string TokenWithoutOwnerRole()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-signing-key-at-least-32-bytes-long"));
        var token = new JwtSecurityToken(
            issuer: "MelamuHoldings", audience: "MelamuHoldings",
            claims: [new Claim(ClaimTypes.NameIdentifier, "1")],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
