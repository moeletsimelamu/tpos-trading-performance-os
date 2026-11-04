using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MelamuHoldings.Infrastructure.Persistence;

namespace MelamuHoldings.IntegrationTests;

// P8.5: a failed import leaves nothing behind, and a row the database could not hold is the row's problem, not the file's
public class CsvImportIntegrityEndpointTests : IClassFixture<ApiFactory>
{
    private const string Header = "TradeDate,EntryTime,Instrument,Direction,EntryPrice,ExitPrice,RiskAmount,PnL,RR,Strategy,Session,DayOfWeek,TradeDuration,Notes,IsManualOverride,DeviationNotes";

    private readonly ApiFactory _factory;

    public CsvImportIntegrityEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static string Row(string date, string time, string instrument, string strategy = "", decimal entry = 100m, decimal pnL = 50m, decimal rr = 1m) =>
        $"{date},{time},{instrument},Long,{entry},101,50,{pnL},{rr},{strategy},London,Monday,5,,false,";

    private static async Task<HttpResponseMessage> Upload(HttpClient client, params string[] rows)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(string.Join("\n", [Header, .. rows])));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "journal.csv");
        return await client.PostAsync("/api/upload", content);
    }

    private static async Task<JsonElement> Imported(HttpClient client, params string[] rows)
    {
        var response = await Upload(client, rows);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<List<string>> Instruments(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/trades")).EnumerateArray().Select(t => t.GetProperty("instrument").GetString()!).ToList();

    private static async Task<List<string>> Strategies(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/strategies")).EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();

    private async Task Sql(string statement)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync(statement);
    }

    [Fact]
    public async Task A_failure_part_way_leaves_no_trades_strategies_or_matches_and_the_file_can_be_imported_again()
    {
        var client = await Owner();
        var manual = await client.PostAsJsonAsync("/api/trades", new
        {
            status = "Closed", tradeDate = "2034-03-06", instrument = "HANDMADE", direction = "Long", entryTime = "09:00:00",
            entryPrice = 100m, exitPrice = 101m, pnL = 50m, riskAmount = 50m, session = "London"
        });
        manual.EnsureSuccessStatusCode();

        // Each new strategy used to save everything so far, so the rows before the failing one were kept
        string[] rows =
        [
            Row("2034-03-06", "09:00", "HANDMADE"),
            Row("2034-03-07", "09:00", "KEPT-A", "Rollback A"),
            Row("2034-03-07", "10:00", "KEPT-B", "Rollback B"),
            Row("2034-03-07", "11:00", "FAILME", "Rollback C")
        ];

        // Only the last row breaks this, and only when the whole file has otherwise gone through
        await Sql("ALTER TABLE dbo.Trades ADD CONSTRAINT CK_FailImport CHECK (Instrument <> 'FAILME');");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Upload(client, rows)).StatusCode);
        }
        finally
        {
            await Sql("ALTER TABLE dbo.Trades DROP CONSTRAINT CK_FailImport;");
        }

        var instruments = await Instruments(client);
        Assert.DoesNotContain("KEPT-A", instruments);
        Assert.DoesNotContain("KEPT-B", instruments);
        Assert.DoesNotContain("FAILME", instruments);
        var strategies = await Strategies(client);
        Assert.DoesNotContain("Rollback A", strategies);
        Assert.DoesNotContain("Rollback B", strategies);
        Assert.DoesNotContain("HANDMADE", (await client.GetFromJsonAsync<JsonElement>("/api/upload/matches")).EnumerateArray().Select(m => m.GetProperty("row").GetProperty("instrument").GetString()));

        var failed = (await client.GetFromJsonAsync<JsonElement>("/api/upload/logs"))[0];
        Assert.Equal(("Failed", 0), (failed.GetProperty("status").GetString(), failed.GetProperty("parsedRows").GetInt32()));

        // Nothing stayed, so the same file imports in full, and then only as duplicates
        var again = await Imported(client, rows);
        Assert.Equal(("Completed", 3, 1, 0), (again.GetProperty("status").GetString(), again.GetProperty("parsedRows").GetInt32(), again.GetProperty("matchedRows").GetInt32(), again.GetProperty("duplicateRows").GetInt32()));
        var third = await Imported(client, rows);
        Assert.Equal((0, 0, 4), (third.GetProperty("parsedRows").GetInt32(), third.GetProperty("matchedRows").GetInt32(), third.GetProperty("duplicateRows").GetInt32()));
    }

    [Theory]
    [InlineData("1000000", "100", "50", "RR")]
    [InlineData("1", "10000000000000", "50", "EntryPrice")]
    [InlineData("1", "100", "10000000000000000", "PnL")]
    public async Task A_row_too_large_for_the_database_is_reported_and_the_others_are_imported(string rr, string entry, string pnL, string named)
    {
        var client = await Other();
        var good = $"GOOD-{named}";

        var result = await Imported(client,
            Row("2034-04-03", "09:00", good),
            $"2034-04-03,10:00,TOO-BIG-{named},Long,{entry},101,50,{pnL},{rr},,London,Monday,5,,false,");

        Assert.Equal(("Completed", 1, 1), (result.GetProperty("status").GetString(), result.GetProperty("parsedRows").GetInt32(), result.GetProperty("failedRows").GetInt32()));
        Assert.Contains($"Row 3: {named}", Assert.Single(result.GetProperty("errors").EnumerateArray()).GetString());
        var instruments = await Instruments(client);
        Assert.Contains(good, instruments);
        Assert.DoesNotContain($"TOO-BIG-{named}", instruments);
    }

    [Theory]
    [InlineData("half past nine", "BAD-TIME")]
    [InlineData("25:00", "LATE-TIME")]
    [InlineData("", "NO-TIME")]
    public async Task An_entry_time_that_cannot_be_read_is_reported_and_not_turned_into_midnight(string time, string instrument)
    {
        var client = await Other();

        var result = await Imported(client, Row("2034-05-01", time, instrument));

        Assert.Equal((0, 1), (result.GetProperty("parsedRows").GetInt32(), result.GetProperty("failedRows").GetInt32()));
        Assert.Contains("Row 2: Invalid EntryTime", Assert.Single(result.GetProperty("errors").EnumerateArray()).GetString());
        Assert.DoesNotContain(instrument, await Instruments(client));
    }

    [Fact]
    public async Task The_same_row_twice_in_one_file_is_imported_once()
    {
        var client = await Other();
        var row = Row("2034-06-05", "09:00", "TWICE");

        var result = await Imported(client, row, row, Row("2034-06-05", "09:01", "TWICE"));

        Assert.Equal((2, 1), (result.GetProperty("parsedRows").GetInt32(), result.GetProperty("duplicateRows").GetInt32()));
        Assert.Equal(2, (await Instruments(client)).Count(i => i == "TWICE"));
    }

    [Fact]
    public async Task The_same_row_twice_matching_a_hand_entered_trade_asks_once()
    {
        var client = await _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
        var manual = await client.PostAsJsonAsync("/api/trades", new
        {
            status = "Closed", tradeDate = "2034-07-03", instrument = "ONE-MATCH", direction = "Long", entryTime = "09:00:00",
            entryPrice = 100m, exitPrice = 101m, pnL = 50m, riskAmount = 50m, session = "London"
        });
        manual.EnsureSuccessStatusCode();
        var row = Row("2034-07-03", "09:00", "ONE-MATCH");

        var result = await Imported(client, row, row);

        Assert.Equal((1, 1), (result.GetProperty("matchedRows").GetInt32(), result.GetProperty("duplicateRows").GetInt32()));
    }
}
