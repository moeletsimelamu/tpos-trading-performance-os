using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Application.DTOs;

namespace MelamuHoldings.UnitTests;

public class CreateTradeDtoValidationTests
{
    private static CreateTradeDto ValidTrade() => new()
    {
        TradeDate = new DateOnly(2026, 10, 5),
        EntryTime = new TimeOnly(9, 35),
        Instrument = "NQ",
        Direction = "long",
        EntryPrice = 20100.25m,
        ExitPrice = 20140.25m,
        RiskAmount = 200,
        PnL = 400,
        RR = 2
    };

    private static List<ValidationResult> Validate(CreateTradeDto dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Valid_trade_has_no_errors()
    {
        Assert.Empty(Validate(ValidTrade()));
    }

    [Theory]
    [InlineData("Sideways")]
    [InlineData("7")]   // Enum.TryParse would accept this
    [InlineData("")]
    public void Direction_must_be_long_or_short(string direction)
    {
        var dto = ValidTrade();
        dto.Direction = direction;

        Assert.Contains(Validate(dto), r => r.MemberNames.Contains(nameof(CreateTradeDto.Direction)));
    }

    [Fact]
    public void Prices_must_be_positive_and_risk_not_negative()
    {
        var dto = ValidTrade();
        dto.EntryPrice = 0;
        dto.ExitPrice = -1;
        dto.RiskAmount = -5;

        var members = Validate(dto).SelectMany(r => r.MemberNames).ToList();

        Assert.Contains(nameof(CreateTradeDto.EntryPrice), members);
        Assert.Contains(nameof(CreateTradeDto.ExitPrice), members);
        Assert.Contains(nameof(CreateTradeDto.RiskAmount), members);
    }

    [Fact]
    public void Instrument_is_required_and_bounded()
    {
        var missing = ValidTrade();
        missing.Instrument = "";
        var tooLong = ValidTrade();
        tooLong.Instrument = new string('X', 51);

        Assert.Contains(Validate(missing), r => r.MemberNames.Contains(nameof(CreateTradeDto.Instrument)));
        Assert.Contains(Validate(tooLong), r => r.MemberNames.Contains(nameof(CreateTradeDto.Instrument)));
    }

    [Fact]
    public void Trade_date_is_required()
    {
        var dto = ValidTrade();
        dto.TradeDate = default;

        Assert.Contains(Validate(dto), r => r.MemberNames.Contains(nameof(CreateTradeDto.TradeDate)));
    }
}
