using System.Text.Json;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.UnitTests;

// How a trade's values are written in its change history: the same way every time, so only real changes show
public class TradeValuesTests
{
    private static Trade Trade() => new()
    {
        Status = TradeStatus.Open, TradeDate = new DateOnly(2031, 9, 1), Instrument = "NQ", EntryTime = new TimeOnly(9, 30),
        EntryPrice = 21100m, RiskAmount = 200m, Session = "New York"
    };

    [Fact]
    public void The_same_number_at_another_precision_is_not_a_change()
    {
        var before = TradeValues.Capture(Trade());
        var after = Trade();
        after.EntryPrice = 21100.00000m;   // as SQL Server returns decimal(18,5)

        Assert.Empty(TradeValues.Diff(before, TradeValues.Capture(after)));
    }

    [Fact]
    public void Changes_are_written_in_the_invariant_culture_with_empty_values_as_null()
    {
        var before = TradeValues.Capture(Trade());
        var after = Trade();
        after.ExitPrice = 21130.25m;
        after.Session = "";
        after.ExitTime = new TimeOnly(10, 20, 5);

        var changes = TradeValues.Diff(before, TradeValues.Capture(after)).ToDictionary(c => c.Field);

        Assert.Equal(new FieldChange("ExitPrice", "Exit price", null, "21130.25"), changes["ExitPrice"]);
        Assert.Equal(new FieldChange("Session", "Session", "New York", null), changes["Session"]);
        Assert.Equal("10:20:05", changes["ExitTime"].After);
        Assert.Equal(3, changes.Count);
    }

    [Theory]
    [InlineData("\"Calm\"", "Calm")]
    [InlineData("\"  \"", null)]
    [InlineData("4", "4")]
    [InlineData("true", "Yes")]
    [InlineData("false", "No")]
    [InlineData("[\"Late entry\",\"Moved stop\"]", "Late entry, Moved stop")]
    [InlineData("[]", null)]
    [InlineData("null", null)]
    public void Journal_answers_are_written_as_the_trader_reads_them(string json, string? expected) =>
        Assert.Equal(expected, TradeValues.Answer(JsonDocument.Parse(json).RootElement));
}
