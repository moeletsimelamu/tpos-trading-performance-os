using CsvHelper.Configuration.Attributes;

namespace MelamuHoldings.Application.Trading;

public class TradeCsvRecord
{
    [Name("TradeDate")]
    public string TradeDate { get; set; } = string.Empty;

    [Name("EntryTime")]
    public string EntryTime { get; set; } = string.Empty;

    [Name("Instrument")]
    public string Instrument { get; set; } = string.Empty;

    [Name("Direction")]
    public string Direction { get; set; } = string.Empty;

    [Name("EntryPrice")]
    public decimal EntryPrice { get; set; }

    [Name("ExitPrice")]
    public decimal ExitPrice { get; set; }

    [Name("RiskAmount")]
    public decimal RiskAmount { get; set; }

    [Name("PnL")]
    public decimal PnL { get; set; }

    [Name("RR")]
    public decimal RR { get; set; }

    [Name("Strategy")]
    public string? Strategy { get; set; }

    [Name("Session")]
    public string Session { get; set; } = string.Empty;

    [Name("DayOfWeek")]
    public string DayOfWeek { get; set; } = string.Empty;

    [Name("TradeDuration")]
    public int TradeDuration { get; set; }

    [Name("Notes")]
    public string? Notes { get; set; }

    [Name("IsManualOverride")]
    public bool IsManualOverride { get; set; }

    [Name("DeviationNotes")]
    public string? DeviationNotes { get; set; }

    // The row's line number in the file (the header is row 1); set by the parser, not read from a column
    [Ignore]
    public int RowNumber { get; set; }
}
