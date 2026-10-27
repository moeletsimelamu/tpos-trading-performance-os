using System.Text;
using MelamuHoldings.Application.Services;

namespace MelamuHoldings.UnitTests;

public class CsvParserServiceTests
{
    private const string Header =
        "TradeDate,EntryTime,Instrument,Direction,EntryPrice,ExitPrice,RiskAmount,PnL,RR,Strategy,Session,DayOfWeek,TradeDuration,Notes,IsManualOverride,DeviationNotes";

    private static CsvParseResult Parse(params string[] rows) =>
        new CsvParserService().Parse(new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', [Header, .. rows]))));

    [Fact]
    public void Parses_valid_row()
    {
        var result = Parse("2026-10-05,09:35,NQ,Long,20100.25,20140.25,200,400,2.0,ORB,New York,Monday,25,,false,");

        var record = Assert.Single(result.ValidRecords);
        Assert.Empty(result.Errors);
        Assert.Equal("NQ", record.Instrument);
        Assert.Equal(400m, record.PnL);
        Assert.Equal(2.0m, record.RR);
        Assert.Equal("ORB", record.Strategy);
    }

    [Theory]
    [InlineData("2026-10-05,09:35,NQ,Sideways,1,2,100,50,0.5,,NY,Monday,5,,false,", "Direction must be 'Long' or 'Short'")]
    [InlineData("not-a-date,09:35,NQ,Long,1,2,100,50,0.5,,NY,Monday,5,,false,", "Invalid TradeDate")]
    [InlineData("2026-10-05,09:35,,Long,1,2,100,50,0.5,,NY,Monday,5,,false,", "Instrument is required")]
    [InlineData("2026-10-05,09:35,NQ,Long,0,2,100,50,0.5,,NY,Monday,5,,false,", "EntryPrice must be greater than zero")]
    [InlineData("2026-10-05,09:35,NQ,Long,1,abc,100,50,0.5,,NY,Monday,5,,false,", "Row 2")]
    public void Rejects_invalid_row_with_row_numbered_error(string row, string expectedMessage)
    {
        var result = Parse(row);

        Assert.Empty(result.ValidRecords);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Row 2:", error);
        Assert.Contains(expectedMessage, error);
    }

    [Fact]
    public void Keeps_valid_rows_when_others_fail()
    {
        var result = Parse(
            "2026-10-05,09:35,NQ,Long,1,2,100,50,0.5,,NY,Monday,5,,false,",
            "2026-10-05,09:40,NQ,Up,1,2,100,50,0.5,,NY,Monday,5,,false,",
            "2026-10-05,09:45,ES,Short,1,2,100,-50,-0.5,,NY,Monday,5,,false,");

        Assert.Equal(2, result.ValidRecords.Count);
        Assert.Equal("Row 3: Direction must be 'Long' or 'Short'.", Assert.Single(result.Errors));
    }
}
