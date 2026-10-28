using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public class TradeResponseDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsImported { get; set; }
    public DateOnly TradeDate { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public string? TradingAccount { get; set; }
    public string? Direction { get; set; }
    public TimeOnly? EntryTime { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? PositionSize { get; set; }
    public decimal? RiskAmount { get; set; }
    public DateOnly? ExitDate { get; set; }
    public TimeOnly? ExitTime { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? PnL { get; set; }
    public decimal? Fees { get; set; }
    public decimal? RR { get; set; }
    public string? Currency { get; set; }
    public string? StrategyName { get; set; }
    public string Session { get; set; } = string.Empty;
    public string DayOfWeek { get; set; } = string.Empty;
    public int? TradeDuration { get; set; }
    public bool IsManualOverride { get; set; }
    public string? DeviationNotes { get; set; }
    public string? Notes { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // The template answers and their questions; included when one trade is read, not in lists
    public TradeJournalDto? Journal { get; set; }

    // The checklist for the trade's strategy, with every answer given; also only when one trade is read
    public TradeChecklistDto? Checklist { get; set; }
}

public record TradeResult(TradeResponseDto Trade, bool Replayed);

// A journal entry as entered. Only date and instrument are always required; what else is needed depends on
// Status (Draft, Open or Closed; docs/TRADING_JOURNAL_DESIGN.md §3). Values that are given are always checked.
public class CreateTradeDto : IValidatableObject
{
    // Draft, Open or Closed. Omitted: Closed when creating (the original contract), unchanged when updating.
    public string? Status { get; set; }

    public DateOnly TradeDate { get; set; }

    [Required, MaxLength(50)]
    public string Instrument { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? TradingAccount { get; set; }

    public string? Direction { get; set; }
    public TimeOnly? EntryTime { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? PositionSize { get; set; }
    public decimal? RiskAmount { get; set; }
    public DateOnly? ExitDate { get; set; }
    public TimeOnly? ExitTime { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? PnL { get; set; }
    public decimal? Fees { get; set; }

    // Leave empty to have it calculated as P&L ÷ risk when both are known
    public decimal? RR { get; set; }

    public string? Currency { get; set; }

    [MaxLength(100)]
    public string? StrategyName { get; set; }

    [MaxLength(50)]
    public string Session { get; set; } = string.Empty;

    // Ignored for new journal entries: the day is taken from TradeDate
    [MaxLength(20)]
    public string DayOfWeek { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int? TradeDuration { get; set; }

    public bool IsManualOverride { get; set; }

    [MaxLength(2000)]
    public string? DeviationNotes { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    // Answers to the journal template's questions; omitted, the trade's answers are left as they are
    public JournalAnswersRequest? Journal { get; set; }

    // Answers to the strategy's rules; omitted or empty, the checklist is left as it is
    public List<RuleAnswerRequest>? Checklist { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TradeDate == default)
            yield return new ValidationResult("TradeDate is required.", [nameof(TradeDate)]);

        if (Status is not null && !new[] { "Draft", "Open", "Closed" }.Contains(Status, StringComparer.OrdinalIgnoreCase))
            yield return new ValidationResult("Status must be 'Draft', 'Open' or 'Closed'. Use the review action to mark a trade reviewed.", [nameof(Status)]);

        // Names only: Enum.TryParse would also accept numeric strings such as "7"
        if (Direction is not null && !Enum.GetNames<TradeDirection>().Contains(Direction, StringComparer.OrdinalIgnoreCase))
            yield return new ValidationResult("Direction must be 'Long' or 'Short'.", [nameof(Direction)]);

        foreach (var (value, name) in new[] { (EntryPrice, nameof(EntryPrice)), (ExitPrice, nameof(ExitPrice)), (StopLoss, nameof(StopLoss)), (TakeProfit, nameof(TakeProfit)), (PositionSize, nameof(PositionSize)) })
            if (value is <= 0)
                yield return new ValidationResult($"{name} must be greater than zero.", [name]);

        if (RiskAmount < 0)
            yield return new ValidationResult("RiskAmount cannot be negative.", [nameof(RiskAmount)]);

        if (Fees < 0)
            yield return new ValidationResult("Fees cannot be negative.", [nameof(Fees)]);

        if (ExitDate is { } exit && exit < TradeDate)
            yield return new ValidationResult("ExitDate cannot be before TradeDate.", [nameof(ExitDate)]);

        if (Currency is not null && !Domain.Finance.Currency.TryFromCode(Currency, out _))
            yield return new ValidationResult($"Currency must be one of {string.Join(", ", Domain.Finance.Currency.Supported.Select(c => c.Code))}.", [nameof(Currency)]);
    }
}

public class UpdateTradeDto : CreateTradeDto { }

public class ReviewTradeDto
{
    [Required, MaxLength(Trade.MaxReviewNotesLength)]
    public string ReviewNotes { get; set; } = string.Empty;
}
