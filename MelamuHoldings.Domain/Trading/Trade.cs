using MelamuHoldings.Domain.Identity;

namespace MelamuHoldings.Domain.Trading;

public class Trade
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? StrategyId { get; set; }
    public int? UploadLogId { get; set; }

    public DateOnly TradeDate { get; set; }
    public TimeOnly EntryTime { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal PnL { get; set; }
    public decimal RR { get; set; }
    public bool IsManualOverride { get; set; }
    public string? DeviationNotes { get; set; }
    public string? Notes { get; set; }
    public int TradeDuration { get; set; }
    public string DayOfWeek { get; set; } = string.Empty;
    public string Session { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Strategy? Strategy { get; set; }
    public UploadLog? UploadLog { get; set; }
}
