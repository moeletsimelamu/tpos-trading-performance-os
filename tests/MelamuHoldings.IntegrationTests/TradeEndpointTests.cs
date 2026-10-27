using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

public class TradeEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TradeEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> OwnerClient() =>
        _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);

    [Fact]
    public async Task Created_trade_is_returned_with_camel_cased_rr()
    {
        var client = await OwnerClient();

        var created = await client.PostAsJsonAsync("/api/trades", ApiClientExtensions.ValidTrade("CREATE-RR"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/trades/{id}");

        // The Angular model depends on this exact property name
        Assert.Equal(2m, fetched.GetProperty("rr").GetDecimal());
        Assert.Equal("CREATE-RR", fetched.GetProperty("instrument").GetString());
        Assert.Equal("ORB", fetched.GetProperty("strategyName").GetString());
    }

    [Fact]
    public async Task Invalid_trade_returns_validation_problem_instead_of_500()
    {
        var client = await OwnerClient();
        var trade = new { tradeDate = "2026-10-05", instrument = "NQ", direction = "Sideways", entryPrice = 0, exitPrice = 1, riskAmount = -5 };

        var response = await client.PostAsJsonAsync("/api/trades", trade);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("Direction", out _));
        Assert.True(errors.TryGetProperty("EntryPrice", out _));
        Assert.True(errors.TryGetProperty("RiskAmount", out _));
    }

    [Fact]
    public async Task Users_cannot_read_change_or_delete_each_others_trades()
    {
        var owner = await OwnerClient();
        var other = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);
        var created = await owner.PostAsJsonAsync("/api/trades", ApiClientExtensions.ValidTrade("PRIVATE"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/trades/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/trades/{id}", ApiClientExtensions.ValidTrade("HIJACKED"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/trades/{id}")).StatusCode);

        var otherTrades = await other.GetFromJsonAsync<JsonElement>("/api/trades");
        Assert.DoesNotContain(otherTrades.EnumerateArray(), t => t.GetProperty("id").GetInt32() == id);

        var stillThere = await owner.GetFromJsonAsync<JsonElement>($"/api/trades/{id}");
        Assert.Equal("PRIVATE", stillThere.GetProperty("instrument").GetString());
    }

    [Fact]
    public async Task Csv_upload_imports_valid_rows_and_reports_failures()
    {
        // A dedicated user keeps this test's trades out of the other tests' assertions
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);
        var csv = """
            TradeDate,EntryTime,Instrument,Direction,EntryPrice,ExitPrice,RiskAmount,PnL,RR,Strategy,Session,DayOfWeek,TradeDuration,Notes,IsManualOverride,DeviationNotes
            2026-10-06,09:40,CSV-A,Long,100,98,200,-400,-2,ORB,New York,Tuesday,12,,false,
            2026-10-06,09:52,CSV-B,Short,98,97,200,200,1,ORB,New York,Tuesday,9,,true,Entered without setup
            2026-10-06,10:30,CSV-C,Sideways,98,97,200,200,1,ORB,New York,Tuesday,9,,false,
            """;
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "journal.csv");

        var response = await client.PostAsync("/api/upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetProperty("parsedRows").GetInt32());
        Assert.Equal(1, result.GetProperty("failedRows").GetInt32());

        var behaviour = await client.GetFromJsonAsync<JsonElement>("/api/analytics/behaviour");
        Assert.Equal(1, behaviour.GetProperty("revengeTradingCount").GetInt32());
        Assert.Equal(1, behaviour.GetProperty("strategyDeviationCount").GetInt32());
    }

    [Fact]
    public async Task Non_csv_upload_is_rejected_with_problem_details()
    {
        var client = await OwnerClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent("hello"u8.ToArray()), "file", "notes.txt");

        var response = await client.PostAsync("/api/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Only CSV files are accepted.", problem.GetProperty("title").GetString());
    }
}
