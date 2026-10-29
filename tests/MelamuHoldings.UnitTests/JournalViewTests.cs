using System.Text;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.UnitTests;

public class JournalViewTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static ImportedRow Row(string instrument = "NQ", string date = "2026-10-20", string time = "09:30", decimal entry = 100m,
        TradeDirection direction = TradeDirection.Long, decimal pnl = 50m) =>
        new(2, DateOnly.Parse(date), TimeOnly.Parse(time), instrument, direction, entry, 101m, 25m, pnl, 2m, "ORB",
            "New York", "Tuesday", 12, false, null, "From the platform");

    private static Trade Manual(Action<Trade>? set = null)
    {
        var t = new Trade { Id = 1, UserId = 1, Status = TradeStatus.Open, TradeDate = DateOnly.Parse("2026-10-20"), Instrument = "nq",
            Direction = TradeDirection.Long, EntryTime = TimeOnly.Parse("09:30:41"), EntryPrice = 100m, Notes = "Mine" };
        set?.Invoke(t);
        return t;
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    public void Accepted_types_are_recognised_by_their_first_bytes(byte[] header, string contentType) =>
        Assert.Equal(contentType, AttachmentType.Detect(header)?.ContentType);

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<html><script>alert(1)</script>")]
    [InlineData("GIF89a")]
    [InlineData("MZ\u0090\u0000")]
    [InlineData("RIFF....AVI ")]
    public void Anything_else_is_refused(string content) => Assert.Null(AttachmentType.Detect(Encoding.Latin1.GetBytes(content)));

    [Theory]
    [InlineData("../../etc/setup.png", "setup.png")]
    [InlineData(@"C:\charts\evil<name>.exe", "evilname.png")]
    [InlineData("...", "attachment.png")]
    [InlineData("chart.pdf", "chart.png")]
    public void File_names_lose_folders_and_unsafe_characters_and_take_the_detected_extension(string uploaded, string kept) =>
        Assert.Equal(kept, new TradeAttachment(1, uploaded, AttachmentType.Png, 10, "k", null, Now).FileName);

    [Fact]
    public void Empty_or_oversized_files_are_refused()
    {
        Assert.Throws<DomainException>(() => new TradeAttachment(1, "a.png", AttachmentType.Png, 0, "k", null, Now));
        Assert.Throws<DomainException>(() => new TradeAttachment(1, "a.png", AttachmentType.Png, TradeAttachment.MaxBytes + 1, "k", null, Now));
    }

    [Fact]
    public void A_row_matches_a_journaled_trade_only_when_nothing_recorded_disagrees()
    {
        Assert.True(Row().Matches(Manual()));   // instrument case and seconds do not matter
        Assert.True(Row().Matches(Manual(t => { t.EntryTime = null; t.Direction = null; })));
        Assert.False(Row(time: "09:31").Matches(Manual()));
        Assert.False(Row(entry: 100.25m).Matches(Manual()));
        Assert.False(Row(direction: TradeDirection.Short).Matches(Manual()));
        Assert.False(Row(date: "2026-10-21").Matches(Manual()));
        Assert.False(Row().Matches(Manual(t => { t.EntryTime = null; t.EntryPrice = null; })));   // too little to go on
        Assert.False(Row().Matches(Manual(t => t.UploadLogId = 3)));   // an imported trade is a duplicate, not a match
    }

    [Fact]
    public void A_row_duplicates_an_imported_trade_with_the_same_values()
    {
        var imported = ImportReconciliationService.ToTrade(Row(), 1, 3, null, Now);
        Assert.True(Row().Duplicates(imported));
        Assert.False(Row(pnl: 51m).Duplicates(imported));
        Assert.False(Row().Duplicates(Manual()));
    }

    [Fact]
    public void Linking_fills_only_empty_fields_and_keeps_notes_and_status()
    {
        var trade = Manual(t => { t.RiskAmount = 40m; t.Session = ""; });
        var match = new ImportMatch(1, 3, trade.Id, Row(), Now);

        var filled = match.Link(trade, strategyId: 9, Now);

        Assert.Equal(["ExitPrice", "PnL", "RR", "TradeDuration", "Session", "Strategy"], filled);
        Assert.Equal(40m, trade.RiskAmount);   // the trader's own value stays
        Assert.Equal("Mine", trade.Notes);
        Assert.Equal(TradeStatus.Open, trade.Status);
        Assert.Equal(50m, trade.PnL);
        Assert.Equal(9, trade.StrategyId);
        Assert.Equal(ImportMatchStatus.Linked, match.Status);
        Assert.Throws<DomainException>(() => match.Dismiss(Now));
    }

    [Fact]
    public async Task The_calendar_totals_completed_trades_and_keeps_unfinished_ones_apart()
    {
        Trade T(string date, decimal? pnl, TradeStatus status)
        {
            var t = TradeFactory.Trade(date, "09:30", pnl ?? 0);
            t.PnL = pnl;
            t.Status = status;
            return t;
        }
        var service = new JournalOverviewService(new InMemoryTradeRepository(
            T("2026-10-05", 100, TradeStatus.Closed), T("2026-10-05", -30, TradeStatus.Reviewed), T("2026-10-05", 500, TradeStatus.Draft),
            T("2026-10-12", null, TradeStatus.Open), T("2026-11-02", 70, TradeStatus.Closed)));

        var month = await service.GetMonthAsync(1, 2026, 10);

        Assert.Equal((2, 70m), (month.CompletedTrades, month.NetPnL));
        Assert.Equal(new CalendarDayDto(DateOnly.Parse("2026-10-05"), 2, 70m, 1, 1, 1, 0), month.Days[0]);
        Assert.Equal(new CalendarDayDto(DateOnly.Parse("2026-10-12"), 0, 0m, 0, 0, 0, 1), month.Days[1]);
        await Assert.ThrowsAsync<DomainException>(() => service.GetMonthAsync(1, 2026, 0));
    }
}
