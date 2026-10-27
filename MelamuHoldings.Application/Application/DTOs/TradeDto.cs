namespace MelamuHoldings.Application.DTOs;

public class TradeResponseDto
{
    public int Id { get; set; }
    public DateOnly TradeDate { get; set; }
    public TimeOnly EntryTime { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal PnL { get; set; }
    public decimal RR { get; set; }
    public string? StrategyName { get; set; }
    public string Session { get; set; } = string.Empty;
    public string DayOfWeek { get; set; } = string.Empty;
    public int TradeDuration { get; set; }
    public bool IsManualOverride { get; set; }
    public string? DeviationNotes { get; set; }
    public string? Notes { get; set; }
}

public class CreateTradeDto
{
    public DateOnly TradeDate { get; set; }
    public TimeOnly EntryTime { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal PnL { get; set; }
    public decimal RR { get; set; }
    public string? StrategyName { get; set; }
    public string Session { get; set; } = string.Empty;
    public string DayOfWeek { get; set; } = string.Empty;
    public int TradeDuration { get; set; }
    public bool IsManualOverride { get; set; }
    public string? DeviationNotes { get; set; }
    public string? Notes { get; set; }
}

public class UpdateTradeDto : CreateTradeDto { }