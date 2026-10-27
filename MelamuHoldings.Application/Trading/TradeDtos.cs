using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

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

public class CreateTradeDto : IValidatableObject
{
    public DateOnly TradeDate { get; set; }
    public TimeOnly EntryTime { get; set; }

    [Required, MaxLength(50)]
    public string Instrument { get; set; } = string.Empty;

    [Required]
    public string Direction { get; set; } = string.Empty;

    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal PnL { get; set; }
    public decimal RR { get; set; }

    [MaxLength(100)]
    public string? StrategyName { get; set; }

    [MaxLength(50)]
    public string Session { get; set; } = string.Empty;

    [MaxLength(20)]
    public string DayOfWeek { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int TradeDuration { get; set; }

    public bool IsManualOverride { get; set; }

    [MaxLength(2000)]
    public string? DeviationNotes { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TradeDate == default)
            yield return new ValidationResult("TradeDate is required.", [nameof(TradeDate)]);

        // Names only: Enum.TryParse would also accept numeric strings such as "7"
        if (!Enum.GetNames<TradeDirection>().Contains(Direction, StringComparer.OrdinalIgnoreCase))
            yield return new ValidationResult("Direction must be 'Long' or 'Short'.", [nameof(Direction)]);

        if (EntryPrice <= 0)
            yield return new ValidationResult("EntryPrice must be greater than zero.", [nameof(EntryPrice)]);

        if (ExitPrice <= 0)
            yield return new ValidationResult("ExitPrice must be greater than zero.", [nameof(ExitPrice)]);

        if (RiskAmount < 0)
            yield return new ValidationResult("RiskAmount cannot be negative.", [nameof(RiskAmount)]);
    }
}

public class UpdateTradeDto : CreateTradeDto { }
