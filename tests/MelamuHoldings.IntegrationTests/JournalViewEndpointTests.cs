using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MelamuHoldings.IntegrationTests;

// Journal J5: overview and calendar, attachments (type, size, access), and CSV import reconciliation
public class JournalViewEndpointTests : IClassFixture<ApiFactory>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% test statement\n");
    private const string CsvHeader = "TradeDate,EntryTime,Instrument,Direction,EntryPrice,ExitPrice,RiskAmount,PnL,RR,Strategy,Session,DayOfWeek,TradeDuration,Notes,IsManualOverride,DeviationNotes";

    private readonly ApiFactory _factory;

    public JournalViewEndpointTests(ApiFactory factory) => _factory = factory;

    private Task<HttpClient> Owner() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword);
    private Task<HttpClient> Other() => _factory.CreateClient().AuthenticateAsync(ApiFactory.OtherEmail, ApiFactory.OtherPassword);

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<int> CreateTrade(HttpClient client, object body) =>
        (await Ok(client.PostAsJsonAsync("/api/trades", body))).GetProperty("id").GetInt32();

    private static object Closed(string instrument, string date, decimal pnL) => new
    {
        status = "Closed", tradeDate = date, instrument, direction = "Long", entryTime = "09:30:00",
        entryPrice = 100m, exitPrice = 101m, pnL, riskAmount = 50m, session = "London"
    };

    private static MultipartFormDataContent FileContent(byte[] bytes, string name, string contentType = "application/octet-stream")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", name } };
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, int tradeId, byte[] bytes, string name, string contentType = "application/octet-stream") =>
        client.PostAsync($"/api/trades/{tradeId}/attachments", FileContent(bytes, name, contentType));

    private static Task<HttpResponseMessage> UploadCsv(HttpClient client, string rows) =>
        client.PostAsync("/api/upload", FileContent(Encoding.UTF8.GetBytes(CsvHeader + "\n" + rows), "platform.csv", "text/csv"));

    private int StoredFiles() => Directory.Exists(_factory.AttachmentsPath) ? Directory.GetFiles(_factory.AttachmentsPath).Length : 0;

    [Fact]
    public async Task The_calendar_counts_only_completed_trades_in_its_totals_and_shows_draft_only_days()
    {
        var client = await Other();
        await CreateTrade(client, Closed("CAL-WIN", "2031-03-03", 120m));
        await CreateTrade(client, Closed("CAL-LOSS", "2031-03-03", -45m));
        await CreateTrade(client, new { status = "Draft", tradeDate = "2031-03-03", instrument = "CAL-DRAFT", session = "London", pnL = 999m });
        await CreateTrade(client, new { status = "Draft", tradeDate = "2031-03-10", instrument = "CAL-ONLY-DRAFT", session = "London" });
        await CreateTrade(client, Closed("CAL-APRIL", "2031-04-01", 10m));

        var month = await Ok(client.GetAsync("/api/journal/calendar?year=2031&month=3"));

        Assert.Equal(2, month.GetProperty("completedTrades").GetInt32());
        Assert.Equal(75m, month.GetProperty("netPnL").GetDecimal());   // the draft's 999 is not counted
        var days = month.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(["2031-03-03", "2031-03-10"], days.Select(d => d.GetProperty("date").GetString()));
        Assert.Equal((2, 1, 1, 1), (days[0].GetProperty("completedTrades").GetInt32(), days[0].GetProperty("wins").GetInt32(),
            days[0].GetProperty("losses").GetInt32(), days[0].GetProperty("drafts").GetInt32()));
        Assert.Equal((0, 1), (days[1].GetProperty("completedTrades").GetInt32(), days[1].GetProperty("drafts").GetInt32()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync("/api/journal/calendar?year=2031&month=13")).StatusCode);
        var owners = await Ok((await Owner()).GetAsync("/api/journal/calendar?year=2031&month=3"));
        Assert.Empty(owners.GetProperty("days").EnumerateArray());
    }

    [Fact]
    public async Task The_overview_lists_what_needs_attention()
    {
        var client = await Other();
        var draft = await CreateTrade(client, new { status = "Draft", tradeDate = "2031-05-02", instrument = "OV-DRAFT", session = "London" });
        var closed = await CreateTrade(client, Closed("OV-CLOSED", "2031-05-02", 30m));

        var overview = await Ok(client.GetAsync("/api/journal/overview"));

        Assert.Contains(overview.GetProperty("draftTrades").EnumerateArray(), t => t.GetProperty("id").GetInt32() == draft);
        Assert.Contains(overview.GetProperty("awaitingReviewTrades").EnumerateArray(), t => t.GetProperty("id").GetInt32() == closed);
        Assert.True(overview.GetProperty("drafts").GetInt32() >= 1);
        Assert.Equal(closed, overview.GetProperty("recent")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Attachments_accept_images_and_pdfs_by_content_and_serve_them_only_to_the_owner()
    {
        var client = await Owner();
        var tradeId = await CreateTrade(client, Closed("ATT-1", "2031-06-02", 10m));

        var image = await Ok(Upload(client, tradeId, Png, "../../setup chart.png"));
        Assert.Equal("setup chart.png", image.GetProperty("fileName").GetString());
        Assert.Equal("image/png", image.GetProperty("contentType").GetString());
        var imageId = image.GetProperty("id").GetInt32();

        // The type comes from the content: a PDF named .png is stored and served as a PDF
        var pdf = await Ok(Upload(client, tradeId, Pdf, "statement.png", "image/png"));
        Assert.Equal("statement.pdf", pdf.GetProperty("fileName").GetString());

        var download = await client.GetAsync($"/api/trades/{tradeId}/attachments/{imageId}");
        Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(Png, await download.Content.ReadAsByteArrayAsync());
        var pdfDownload = await client.GetAsync($"/api/trades/{tradeId}/attachments/{pdf.GetProperty("id").GetInt32()}");
        Assert.Equal("attachment", pdfDownload.Content.Headers.ContentDisposition?.DispositionType);

        var trade = await Ok(client.GetAsync($"/api/trades/{tradeId}"));
        Assert.Equal(2, trade.GetProperty("attachments").GetArrayLength());

        // Another trader sees nothing and can change nothing
        var other = await Other();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/trades/{tradeId}/attachments/{imageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/trades/{tradeId}/attachments/{imageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(other, tradeId, Png, "x.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync($"/api/trades/{tradeId}/attachments/{imageId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/trades/{tradeId}/attachments/{imageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/trades/{tradeId}/attachments/{imageId}")).StatusCode);
    }

    [Fact]
    public async Task Unsafe_oversized_and_surplus_files_are_refused()
    {
        var client = await Owner();
        var tradeId = await CreateTrade(client, Closed("ATT-2", "2031-06-03", 10m));

        var svg = await Upload(client, tradeId, Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"), "chart.svg", "image/svg+xml");
        var html = await Upload(client, tradeId, Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "chart.png", "image/png");
        var big = new byte[5 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        var oversized = await Upload(client, tradeId, big, "huge.png");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, svg.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, html.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/trades/{tradeId}/attachments", new MultipartFormDataContent())).StatusCode);

        for (var i = 0; i < 10; i++) await Ok(Upload(client, tradeId, Png, $"chart-{i}.png"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Upload(client, tradeId, Png, "eleventh.png")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_trade_removes_its_files()
    {
        var client = await Owner();
        var tradeId = await CreateTrade(client, Closed("ATT-3", "2031-06-04", 10m));
        var before = StoredFiles();
        await Ok(Upload(client, tradeId, Png, "a.png"));
        await Ok(Upload(client, tradeId, Pdf, "b.pdf"));
        Assert.Equal(before + 2, StoredFiles());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/trades/{tradeId}")).StatusCode);

        Assert.Equal(before, StoredFiles());
    }

    [Fact]
    public async Task An_import_never_duplicates_a_journaled_trade_and_links_only_what_is_missing()
    {
        var client = await Other();
        var manual = await CreateTrade(client, new
        {
            status = "Open", tradeDate = "2031-07-07", instrument = "REC-NQ", direction = "Long", entryTime = "09:30:00",
            entryPrice = 18000m, session = "", notes = "My own notes"
        });
        var rows = """
            2031-07-07,09:30,rec-nq,Long,18000,18040,200,800,4,ORB,New York,Monday,25,From the platform,false,
            2031-07-07,11:00,REC-ES,Short,5000,4990,100,500,5,ORB,New York,Monday,15,,false,
            """;

        var first = await Ok(UploadCsv(client, rows));
        Assert.Equal((1, 1, 0), (first.GetProperty("parsedRows").GetInt32(), first.GetProperty("matchedRows").GetInt32(), first.GetProperty("duplicateRows").GetInt32()));

        // The same file again: the imported row and the row awaiting a decision are both skipped
        var again = await Ok(UploadCsv(client, rows));
        Assert.Equal((0, 0, 2), (again.GetProperty("parsedRows").GetInt32(), again.GetProperty("matchedRows").GetInt32(), again.GetProperty("duplicateRows").GetInt32()));

        var match = Assert.Single((await Ok(client.GetAsync("/api/upload/matches"))).EnumerateArray());
        Assert.Equal(manual, match.GetProperty("trade").GetProperty("id").GetInt32());
        Assert.Equal(2, match.GetProperty("rowNumber").GetInt32());
        var matchId = match.GetProperty("id").GetInt32();

        // Another trader cannot see or decide it
        var owner = await Owner();
        Assert.Empty((await Ok(owner.GetAsync("/api/upload/matches"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/upload/matches/{matchId}/link", null)).StatusCode);

        var linked = await Ok(client.PostAsync($"/api/upload/matches/{matchId}/link", null));
        var filled = linked.GetProperty("filledFields").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Contains("ExitPrice", filled);
        Assert.Contains("PnL", filled);
        Assert.Contains("Session", filled);
        Assert.DoesNotContain("EntryPrice", filled);

        var trade = await Ok(client.GetAsync($"/api/trades/{manual}"));
        Assert.Equal("My own notes", trade.GetProperty("notes").GetString());
        Assert.Equal("Open", trade.GetProperty("status").GetString());   // closing it stays the trader's decision
        Assert.Equal(800m, trade.GetProperty("pnL").GetDecimal());
        Assert.Equal("REC-NQ", trade.GetProperty("instrument").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync($"/api/upload/matches/{matchId}/link", null)).StatusCode);

        var trades = await Ok(client.GetAsync("/api/trades"));
        Assert.Single(trades.EnumerateArray(), t => t.GetProperty("instrument").GetString()!.Equals("REC-NQ", StringComparison.OrdinalIgnoreCase));
        Assert.Single(trades.EnumerateArray(), t => t.GetProperty("instrument").GetString() == "REC-ES");
    }

    [Fact]
    public async Task A_matched_row_can_be_imported_separately_or_left_out()
    {
        var client = await Other();
        await CreateTrade(client, new { status = "Draft", tradeDate = "2031-08-08", instrument = "SEP-1", entryPrice = 50m, session = "London" });
        await CreateTrade(client, new { status = "Draft", tradeDate = "2031-08-08", instrument = "SEP-2", entryTime = "10:00:00", session = "London" });
        await CreateTrade(client, new { status = "Draft", tradeDate = "2031-08-08", instrument = "SEP-3", session = "London" });   // nothing to match on

        var result = await Ok(UploadCsv(client, """
            2031-08-08,09:00,SEP-1,Long,50,51,10,20,2,,London,Friday,5,,false,
            2031-08-08,10:00,SEP-2,Short,70,69,10,20,2,,London,Friday,5,,false,
            2031-08-08,11:00,SEP-3,Long,30,31,10,20,2,,London,Friday,5,,false,
            """));
        Assert.Equal((1, 2), (result.GetProperty("parsedRows").GetInt32(), result.GetProperty("matchedRows").GetInt32()));

        var matches = (await Ok(client.GetAsync("/api/upload/matches"))).EnumerateArray()
            .Where(m => m.GetProperty("row").GetProperty("instrument").GetString()!.StartsWith("SEP-")).ToList();
        var one = matches.Single(m => m.GetProperty("row").GetProperty("instrument").GetString() == "SEP-1").GetProperty("id").GetInt32();
        var two = matches.Single(m => m.GetProperty("row").GetProperty("instrument").GetString() == "SEP-2").GetProperty("id").GetInt32();

        var imported = await Ok(client.PostAsync($"/api/upload/matches/{one}/import", null));
        Assert.Equal("ImportedSeparately", imported.GetProperty("status").GetString());
        var created = imported.GetProperty("createdTradeId").GetInt32();
        Assert.True((await Ok(client.GetAsync($"/api/trades/{created}"))).GetProperty("isImported").GetBoolean());

        Assert.Equal("Dismissed", (await Ok(client.PostAsync($"/api/upload/matches/{two}/dismiss", null))).GetProperty("status").GetString());
        Assert.DoesNotContain((await Ok(client.GetAsync("/api/upload/matches"))).EnumerateArray(), m => m.GetProperty("id").GetInt32() is var id && (id == one || id == two));

        // Concurrent decisions on one row: one wins, the rest are refused, and only one trade is created
        await CreateTrade(client, new { status = "Draft", tradeDate = "2031-08-09", instrument = "SEP-RACE", entryPrice = 10m, session = "London" });
        await Ok(UploadCsv(client, "2031-08-09,09:00,SEP-RACE,Long,10,11,10,20,2,,London,Saturday,5,,false,"));
        var race = (await Ok(client.GetAsync("/api/upload/matches"))).EnumerateArray()
            .Single(m => m.GetProperty("row").GetProperty("instrument").GetString() == "SEP-RACE").GetProperty("id").GetInt32();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsync($"/api/upload/matches/{race}/import", null)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity }));
        var raceTrades = (await Ok(client.GetAsync("/api/trades"))).EnumerateArray().Count(t => t.GetProperty("instrument").GetString() == "SEP-RACE");
        Assert.Equal(2, raceTrades);   // the journaled draft and exactly one import
    }
}
