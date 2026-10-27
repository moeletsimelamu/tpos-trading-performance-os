using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static MelamuHoldings.IntegrationTests.FinanceApi;

namespace MelamuHoldings.IntegrationTests;

// Helpers for the payout → allocation workflow
internal static class PayoutApi
{
    public const int WealthFund = 4, OwnersDistribution = 5, DefaultPolicyId = 1;

    public static async Task<JsonElement> OkJsonAsync(this Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{response.RequestMessage?.RequestUri} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.JsonAsync();
    }

    public static Task<JsonElement> RequestPayoutAsync(this HttpClient client, decimal amount, string currency = "USD") =>
        client.PostAsync("/api/payouts", new { source = "Test Prop Firm", currency, amount, requestedOn = Today }, Key()).OkJsonAsync();

    // Requests, approves and receives a payout in one go; returns the received payout
    public static async Task<JsonElement> ReceivedPayoutAsync(this HttpClient client, int accountId, decimal amount, decimal? fee = null)
    {
        var payout = await client.RequestPayoutAsync(amount + (fee ?? 0));
        var id = payout.Id();
        await client.PostAsJsonAsync($"/api/payouts/{id}/approve", new { date = Today }).OkJsonAsync();
        return await client.PostAsync($"/api/payouts/{id}/receive", new { accountId, amountCredited = amount, fee, date = Today }, Key()).OkJsonAsync();
    }

    public static Task<JsonElement> PreviewAsync(this HttpClient client, int payoutId, int? policyId = null) =>
        client.PostAsJsonAsync("/api/allocations/preview", new { payoutId, policyId }).OkJsonAsync();

    public static Task<HttpResponseMessage> ConfirmAsync(this HttpClient client, int payoutId, int policyId, string key) =>
        client.PostAsync("/api/allocations", new { payoutId, policyId }, key);

    public static Task<HttpResponseMessage> SettleAsync(this HttpClient client, int allocationId, int lineId, object body, string? key = null) =>
        client.PostAsync($"/api/allocations/{allocationId}/lines/{lineId}/settle", body, key ?? Key());

    public static int Id(this JsonElement element) => element.GetProperty("id").GetInt32();
    public static string Status(this JsonElement element) => element.GetProperty("status").GetString()!;
    public static decimal Amount(this JsonElement element, string property) => element.GetProperty(property).GetProperty("amount").GetDecimal();

    public static JsonElement Line(this JsonElement allocation, int fundId) =>
        allocation.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("fundId").GetInt32() == fundId);

    public static decimal[] LineAmounts(this JsonElement allocationOrPreview) =>
        allocationOrPreview.GetProperty("lines").EnumerateArray().Select(l => l.Amount("amount")).ToArray();

    public static string Key() => Guid.NewGuid().ToString("N");
}

// PLAN.md §8: the first-milestone scenario, end to end through the API, in a database of its own
public class AllocationMilestoneTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AllocationMilestoneTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Payout_to_allocation_to_settlement_reconciles_every_balance()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

        // 1–2. Funds are seeded; two USD accounts with opening balances
        var operating = await client.CreateAccountAsync("Operating Bank", "USD");
        var reserve = await client.CreateAccountAsync("Reserve Bank", "USD");
        await client.RecordAsync("opening-balance", new { accountId = operating, fundId = OperatingCapital, amount = 500m, date = Today, description = "Carried over" });
        await client.RecordAsync("opening-balance", new { accountId = reserve, fundId = Continuity, amount = 200m, date = Today, description = "Carried over" });

        // 3. Policy v1 (40/30/20/10) is active out of the box
        var policies = await client.GetAsync("/api/allocation-policies").OkJsonAsync();
        var v1 = Assert.Single(policies.EnumerateArray(), p => p.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, v1.GetProperty("version").GetInt32());
        Assert.Equal([40m, 30m, 20m, 10m], v1.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("percentage").GetDecimal()));

        // 4. A request is not money: no cash moves
        var payout = await client.RequestPayoutAsync(1350m);
        Assert.Equal("Requested", payout.Status());
        Assert.Equal(700m, await CashUsdAsync(client));

        // 5. Approved is still not money; received posts income to Unallocated
        await client.PostAsJsonAsync($"/api/payouts/{payout.Id()}/approve", new { date = Today }).OkJsonAsync();
        Assert.Equal(700m, await CashUsdAsync(client));
        var received = await client.PostAsync($"/api/payouts/{payout.Id()}/receive", new { accountId = operating, amountCredited = 1350m, date = Today }, PayoutApi.Key()).OkJsonAsync();
        Assert.Equal("Received", received.Status());
        Assert.Equal(1350m, (await client.AccountAsync(operating)).FundBalance(Unallocated));

        // 6. Preview stores nothing
        var preview = await client.PreviewAsync(payout.Id());
        Assert.Equal([540m, 405m, 270m, 135m], preview.LineAmounts());
        Assert.Empty((await client.GetAsync("/api/allocations").OkJsonAsync()).EnumerateArray());

        // 7. Confirm
        var key = PayoutApi.Key();
        var confirmed = await client.ConfirmAsync(payout.Id(), PolicyOf(preview), key);
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        var allocation = await confirmed.JsonAsync();
        Assert.Equal("Confirmed", allocation.Status());
        Assert.Equal(1350m, allocation.Amount("outstanding"));

        // 8. Earmark 540 to Operating Capital in place; physically transfer 405 to the reserve bank
        await client.SettleAsync(allocation.Id(), allocation.Line(OperatingCapital).Id(), new { method = "Earmark", amount = 540m, date = Today }).OkJsonAsync();
        allocation = await client.SettleAsync(allocation.Id(), allocation.Line(Continuity).Id(),
            new { method = "Transfer", amount = 405m, toAccountId = reserve, date = Today }).OkJsonAsync();
        Assert.Equal("PartiallySettled", allocation.Status());

        // 9. Settle the rest
        await client.SettleAsync(allocation.Id(), allocation.Line(PayoutApi.WealthFund).Id(), new { method = "Earmark", amount = 270m, date = Today }).OkJsonAsync();
        allocation = await client.SettleAsync(allocation.Id(), allocation.Line(PayoutApi.OwnersDistribution).Id(), new { method = "Earmark", amount = 135m, date = Today }).OkJsonAsync();
        Assert.Equal("Reconciled", allocation.Status());
        Assert.Equal(0m, allocation.Amount("outstanding"));

        // 10. Balances
        var op = await client.AccountAsync(operating);
        var res = await client.AccountAsync(reserve);
        Assert.Equal(1445m, op.Balance());                          // 500 + 1350 − 405
        Assert.Equal(605m, res.Balance());                          // 200 + 405
        Assert.Equal(0m, op.FundBalance(Unallocated));
        Assert.Equal(1040m, op.FundBalance(OperatingCapital));     // 500 opening + 540
        Assert.Equal(270m, op.FundBalance(PayoutApi.WealthFund));
        Assert.Equal(135m, op.FundBalance(PayoutApi.OwnersDistribution));
        Assert.Equal(605m, res.FundBalance(Continuity));           // 200 opening + 405

        // 11. Same key: the original allocation comes back; a new key: refused
        var replay = await client.ConfirmAsync(payout.Id(), PolicyOf(preview), key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(allocation.Id(), (await replay.JsonAsync()).Id());
        Assert.Equal(HttpStatusCode.Conflict, (await client.ConfirmAsync(payout.Id(), PolicyOf(preview), PayoutApi.Key())).StatusCode);

        // 12. A new policy applies to new allocations only
        var v2 = await client.PostAsJsonAsync("/api/allocation-policies", new
        {
            activate = true,
            lines = new[] { new { fundId = OperatingCapital, percentage = 50m }, new { fundId = Continuity, percentage = 25m },
                            new { fundId = PayoutApi.WealthFund, percentage = 15m }, new { fundId = PayoutApi.OwnersDistribution, percentage = 10m } }
        }).OkJsonAsync();
        Assert.Equal(2, v2.GetProperty("version").GetInt32());
        var second = await client.ReceivedPayoutAsync(operating, 1000m);
        var secondPreview = await client.PreviewAsync(second.Id());
        Assert.Equal(2, secondPreview.GetProperty("policyVersion").GetInt32());
        var secondAllocation = await (await client.ConfirmAsync(second.Id(), v2.Id(), PayoutApi.Key())).JsonAsync();
        Assert.Equal([500m, 250m, 150m, 100m], secondAllocation.LineAmounts());

        var first = await client.GetAsync($"/api/allocations/{allocation.Id()}").OkJsonAsync();
        Assert.Equal(1, first.GetProperty("policyVersion").GetInt32());
        Assert.Equal([540m, 405m, 270m, 135m], first.LineAmounts());

        // 13. No cent created or lost on awkward amounts (previewed against v1, policy id 1)
        Assert.Equal([400.01m, 300m, 200m, 100m], (await client.PreviewAsync((await client.ReceivedPayoutAsync(operating, 1000.01m)).Id(), 1)).LineAmounts());
        Assert.Equal([0.01m, 0.01m, 0.01m, 0m], (await client.PreviewAsync((await client.ReceivedPayoutAsync(operating, 0.03m)).Id(), 1)).LineAmounts());
    }

    private static int PolicyOf(JsonElement preview) => preview.GetProperty("policyId").GetInt32();

    private static async Task<decimal> CashUsdAsync(HttpClient client)
    {
        var balances = await client.GetAsync("/api/balances").OkJsonAsync();
        return balances.GetProperty("cashTotals").EnumerateArray()
            .Where(m => m.GetProperty("currency").GetString() == "USD").Select(m => m.GetProperty("amount").GetDecimal()).SingleOrDefault();
    }
}

public class PayoutRuleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PayoutRuleTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    [Fact]
    public async Task A_payout_cannot_be_received_before_it_is_approved()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Payouts"), "USD");
        var payout = await client.RequestPayoutAsync(100m);

        var response = await client.PostAsync($"/api/payouts/{payout.Id()}/receive", new { accountId = account, amountCredited = 100m, date = Today }, PayoutApi.Key());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0m, (await client.AccountAsync(account)).Balance());
    }

    [Fact]
    public async Task A_fee_deducted_on_receipt_is_recorded_beside_the_gross_income()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Payouts"), "USD");

        var payout = await client.ReceivedPayoutAsync(account, 1340m, fee: 10m);

        Assert.Equal(1340m, payout.Amount("credited"));
        Assert.Equal(10m, payout.Amount("fee"));
        Assert.Equal(1340m, (await client.AccountAsync(account)).Balance());
        var receipt = await client.GetAsync($"/api/transactions/{payout.GetProperty("incomeTransactionId").GetInt32()}").OkJsonAsync();
        var entries = receipt.GetProperty("entries").EnumerateArray().Select(e => (e.GetProperty("amount").GetDecimal(), e.GetProperty("role").GetString())).ToList();
        Assert.Equal([(1350m, "Principal"), (-10m, "Fee")], entries);
        // The allocation splits only what was credited
        Assert.Equal(1340m, (await client.PreviewAsync(payout.Id())).Amount("total"));
    }

    [Fact]
    public async Task A_usd_payout_credited_in_rand_records_the_effective_rate()
    {
        var client = await Owner();
        var zar = await client.CreateAccountAsync(Unique("Operating"), "ZAR");
        var payout = await client.RequestPayoutAsync(1000m);
        await client.PostAsJsonAsync($"/api/payouts/{payout.Id()}/approve", new { date = Today }).OkJsonAsync();

        var received = await client.PostAsync($"/api/payouts/{payout.Id()}/receive",
            new { accountId = zar, amountCredited = 18100m, fee = 150m, date = Today }, PayoutApi.Key()).OkJsonAsync();

        Assert.Equal("ZAR", received.GetProperty("credited").GetProperty("currency").GetString());
        Assert.Equal(18.25m, received.GetProperty("effectiveRate").GetDecimal());
        Assert.Equal(18100m, (await client.AccountAsync(zar)).Balance());
    }

    [Fact]
    public async Task Concurrent_confirmations_with_different_keys_allocate_a_payout_once()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Payouts"), "USD");
        var payout = await client.ReceivedPayoutAsync(account, 900m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.ConfirmAsync(payout.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key())));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var allocations = await client.GetAsync("/api/allocations").OkJsonAsync();
        Assert.Single(allocations.EnumerateArray(), a => a.GetProperty("payoutId").GetInt32() == payout.Id());
    }

    // Regression (CI, 2026-10-28): a same-key retry could pass the key check before the first request committed,
    // then fail the state check after it did ("already allocated"/"more than outstanding") instead of replaying
    [Fact]
    public async Task Concurrent_same_key_confirmations_and_settlements_each_happen_once()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Payouts"), "USD");
        var payout = await client.ReceivedPayoutAsync(account, 500m);

        var confirmKey = PayoutApi.Key();
        var confirms = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.ConfirmAsync(payout.Id(), PayoutApi.DefaultPolicyId, confirmKey)));
        Assert.All(confirms, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));
        Assert.Single(confirms, r => r.StatusCode == HttpStatusCode.Created);
        var allocation = await confirms.Single(r => r.StatusCode == HttpStatusCode.Created).JsonAsync();

        var settleKey = PayoutApi.Key();
        var line = allocation.Line(OperatingCapital).Id();
        var settles = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            client.SettleAsync(allocation.Id(), line, new { method = "Earmark", amount = 200m, date = Today }, settleKey)));
        Assert.All(settles, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));

        var settled = await client.GetAsync($"/api/allocations/{allocation.Id()}").OkJsonAsync();
        Assert.Equal(200m, settled.Line(OperatingCapital).Amount("settled"));
        Assert.Single(settled.Line(OperatingCapital).GetProperty("settlements").EnumerateArray());
    }

    [Fact]
    public async Task Retrying_a_receipt_with_the_same_key_posts_it_once()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Payouts"), "USD");
        var payout = await client.RequestPayoutAsync(250m);
        await client.PostAsJsonAsync($"/api/payouts/{payout.Id()}/approve", new { date = Today }).OkJsonAsync();
        var key = PayoutApi.Key();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            client.PostAsync($"/api/payouts/{payout.Id()}/receive", new { accountId = account, amountCredited = 250m, date = Today }, key)));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode, r.StatusCode.ToString()));
        Assert.Equal(250m, (await client.AccountAsync(account)).Balance());
    }

    [Fact]
    public async Task A_receipt_is_undone_from_the_payout_never_from_the_ledger_and_not_while_allocated()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Payouts"), "USD");
        var payout = await client.ReceivedPayoutAsync(account, 300m);
        var incomeId = payout.GetProperty("incomeTransactionId").GetInt32();

        var viaLedger = await client.PostAsync($"/api/transactions/{incomeId}/reverse", new { date = Today, reason = "Wrong" }, PayoutApi.Key());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, viaLedger.StatusCode);

        var allocation = await (await client.ConfirmAsync(payout.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key())).JsonAsync();
        var blocked = await client.PostAsync($"/api/payouts/{payout.Id()}/undo-receipt", new { date = Today, reason = "Wrong amount" }, PayoutApi.Key());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

        await client.PostAsJsonAsync($"/api/allocations/{allocation.Id()}/cancel", new { reason = "Receipt was wrong" }).OkJsonAsync();
        var undone = await client.PostAsync($"/api/payouts/{payout.Id()}/undo-receipt", new { date = Today, reason = "Wrong amount" }, PayoutApi.Key()).OkJsonAsync();

        Assert.Equal("Approved", undone.Status());
        Assert.Equal(0m, (await client.AccountAsync(account)).Balance());
    }

    [Fact]
    public async Task Settlements_are_checked_and_a_reversed_settlement_reopens_its_line()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Payouts"), "USD");
        var payout = await client.ReceivedPayoutAsync(account, 100m);
        var allocation = await (await client.ConfirmAsync(payout.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key())).JsonAsync();
        var operatingLine = allocation.Line(OperatingCapital).Id();

        var over = await client.SettleAsync(allocation.Id(), operatingLine, new { method = "Earmark", amount = 40.01m, date = Today });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, over.StatusCode);

        // Spend some of the unallocated money elsewhere: the remaining lines can no longer all be settled
        await client.RecordAsync("expense", new { accountId = account, fundId = Unallocated, amount = 70m, date = Today, description = "Spent before allocating" });
        var short_ = await client.SettleAsync(allocation.Id(), operatingLine, new { method = "Earmark", amount = 40m, date = Today });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, short_.StatusCode);
        Assert.Contains("holds only", await short_.Content.ReadAsStringAsync());

        var settled = await client.SettleAsync(allocation.Id(), allocation.Line(PayoutApi.OwnersDistribution).Id(), new { method = "Earmark", amount = 10m, date = Today }).OkJsonAsync();
        Assert.Equal("PartiallySettled", settled.Status());
        var transactionId = settled.Line(PayoutApi.OwnersDistribution).GetProperty("settlements")[0].GetProperty("transactionId").GetInt32();

        await client.PostAsync($"/api/transactions/{transactionId}/reverse", new { date = Today, reason = "Wrong fund" }, PayoutApi.Key()).OkJsonAsync();

        var reopened = await client.GetAsync($"/api/allocations/{allocation.Id()}").OkJsonAsync();
        Assert.Equal("Confirmed", reopened.Status());
        Assert.Equal(10m, reopened.Line(PayoutApi.OwnersDistribution).Amount("outstanding"));
        Assert.True(reopened.Line(PayoutApi.OwnersDistribution).GetProperty("settlements")[0].GetProperty("reversed").GetBoolean());
    }

    [Fact]
    public async Task A_line_settled_by_exchange_records_the_fee_and_both_rates()
    {
        var client = await Owner();
        var usd = await client.CreateAccountAsync(Unique("Payouts"), "USD");
        var zar = await client.CreateAccountAsync(Unique("Operating"), "ZAR");
        var payout = await client.ReceivedPayoutAsync(usd, 1000m);
        var allocation = await (await client.ConfirmAsync(payout.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key())).JsonAsync();

        var settled = await client.SettleAsync(allocation.Id(), allocation.Line(OperatingCapital).Id(), new
        {
            method = "Exchange", amount = 390m, toAccountId = zar, amountReceived = 7117.50m, fee = 10m, feeChargedTo = "Sending", quotedRate = 18.30m, date = Today
        }).OkJsonAsync();

        var line = settled.Line(OperatingCapital);
        Assert.Equal(10m, line.Amount("outstanding"));  // 400 − 390 settled; the fee does not count towards the line
        var tx = await client.GetAsync($"/api/transactions/{line.GetProperty("settlements")[0].GetProperty("transactionId").GetInt32()}").OkJsonAsync();
        Assert.Equal(18.25m, tx.GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(18.30m, tx.GetProperty("quotedRate").GetDecimal());
        Assert.Contains(tx.GetProperty("entries").EnumerateArray(), e => e.GetProperty("role").GetString() == "Fee" && e.GetProperty("amount").GetDecimal() == -10m);
        Assert.Equal(600m, (await client.AccountAsync(usd)).Balance());               // 1000 − 390 − 10
        Assert.Equal(7117.50m, (await client.AccountAsync(zar)).FundBalance(OperatingCapital));
    }

    [Fact]
    public async Task Policies_must_total_100_percent_and_activation_moves_to_the_new_version()
    {
        var client = await Owner();

        var invalid = await client.PostAsJsonAsync("/api/allocation-policies", new { lines = new[] { new { fundId = OperatingCapital, percentage = 60m }, new { fundId = Continuity, percentage = 30m } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        var created = await client.PostAsJsonAsync("/api/allocation-policies", new { lines = new[] { new { fundId = OperatingCapital, percentage = 70m }, new { fundId = Continuity, percentage = 30m } } }).OkJsonAsync();
        Assert.False(created.GetProperty("isActive").GetBoolean());

        await client.PostAsJsonAsync($"/api/allocation-policies/{created.Id()}/activate", new { }).OkJsonAsync();
        var policies = await client.GetAsync("/api/allocation-policies").OkJsonAsync();
        Assert.Equal(created.Id(), Assert.Single(policies.EnumerateArray(), p => p.GetProperty("isActive").GetBoolean()).Id());

        // Put the default back so other tests in this class see 40/30/20/10
        await client.PostAsJsonAsync($"/api/allocation-policies/{PayoutApi.DefaultPolicyId}/activate", new { }).OkJsonAsync();
    }

    [Fact]
    public async Task A_currency_exchange_can_carry_a_fee_and_a_quoted_rate()
    {
        var client = await Owner();
        var usd = await client.CreateAccountAsync(Unique("Wallet"), "USD");
        var zar = await client.CreateAccountAsync(Unique("Bank"), "ZAR");
        await client.RecordAsync("income", new { accountId = usd, fundId = Unallocated, amount = 500m, date = Today, description = "Seed" });

        var exchange = await client.RecordAsync("exchange", new
        {
            fromAccountId = usd, fromFundId = Unallocated, amountSent = 400m, toAccountId = zar, toFundId = Unallocated,
            amountReceived = 7300m, fee = 85m, feeChargedTo = "Receiving", quotedRate = 18.4m, date = Today, description = "Convert"
        });

        Assert.Equal(18.25m, exchange.GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(18.4m, exchange.GetProperty("quotedRate").GetDecimal());
        Assert.Equal(7215m, (await client.AccountAsync(zar)).Balance());  // 7300 received − 85 fee charged by the receiving bank
        Assert.Equal(100m, (await client.AccountAsync(usd)).Balance());
    }

    [Fact]
    public async Task Payout_changes_are_audited()
    {
        var client = await Owner();
        var account = await client.CreateAccountAsync(Unique("Payouts"), "USD");
        var payout = await client.ReceivedPayoutAsync(account, 75m);
        await client.ConfirmAsync(payout.Id(), PayoutApi.DefaultPolicyId, PayoutApi.Key());

        var events = (await client.GetAsync("/api/audit-events?pageSize=100").OkJsonAsync()).GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("entityId").GetString() == payout.Id().ToString() && e.GetProperty("entityType").GetString() == "Payout")
            .Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Equal(["payout.received", "payout.approved", "payout.requested"], events);
    }
}
