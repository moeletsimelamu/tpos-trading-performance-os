using MelamuHoldings.Domain.Common;
using MelamuHoldings.Domain.Identity;

namespace MelamuHoldings.Domain.Trading;

// Draft → Open → Closed → Reviewed. Only Closed and Reviewed trades count in analytics (docs/TRADING_JOURNAL_DESIGN.md §3).
public enum TradeStatus
{
    Draft,
    Open,
    Closed,
    Reviewed
}

public class Trade
{
    public const int MaxReviewNotesLength = 4000;

    public int Id { get; set; }
    public int UserId { get; set; }
    public int? StrategyId { get; set; }
    public int? UploadLogId { get; set; }

    // Imported trades and trades created before the journal are complete records
    public TradeStatus Status { get; set; } = TradeStatus.Closed;

    public DateOnly TradeDate { get; set; }
    public string Instrument { get; set; } = string.Empty;
    public string? TradingAccount { get; set; }

    // Execution: empty until recorded. Nothing is filled in with a placeholder value.
    public TradeDirection? Direction { get; set; }
    public TimeOnly? EntryTime { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? PositionSize { get; set; }
    public decimal? RiskAmount { get; set; }
    public DateOnly? ExitDate { get; set; }
    public TimeOnly? ExitTime { get; set; }
    public decimal? ExitPrice { get; set; }

    // Net realised P&L as the platform reports it; fees are recorded beside it, not subtracted again
    public decimal? PnL { get; set; }
    public decimal? Fees { get; set; }

    // R-multiple: P&L ÷ risk
    public decimal? RR { get; set; }

    // Currency of P&L, risk and fees when stated (imports have none)
    public string? Currency { get; set; }

    public bool IsManualOverride { get; set; }
    public string? DeviationNotes { get; set; }
    public string? Notes { get; set; }
    public int? TradeDuration { get; set; }
    public string DayOfWeek { get; set; } = string.Empty;
    public string Session { get; set; } = string.Empty;

    // The trader's own post-trade review
    public string? ReviewNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? IdempotencyKey { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Strategy? Strategy { get; set; }
    public UploadLog? UploadLog { get; set; }

    public bool IsImported => UploadLogId is not null;
    public bool CountsInAnalytics => Status is TradeStatus.Closed or TradeStatus.Reviewed;

    // The fields a status needs that this trade does not have yet, by name
    public IReadOnlyList<string> MissingFor(TradeStatus status)
    {
        var missing = new List<string>();
        if (TradeDate == default) missing.Add(nameof(TradeDate));
        if (string.IsNullOrWhiteSpace(Instrument)) missing.Add(nameof(Instrument));
        if (status == TradeStatus.Draft) return missing;

        if (Direction is null) missing.Add(nameof(Direction));
        if (EntryTime is null) missing.Add(nameof(EntryTime));
        if (EntryPrice is null) missing.Add(nameof(EntryPrice));
        if (status == TradeStatus.Open) return missing;

        if (ExitPrice is null) missing.Add(nameof(ExitPrice));
        if (PnL is null) missing.Add(nameof(PnL));
        if (status == TradeStatus.Reviewed && string.IsNullOrWhiteSpace(ReviewNotes)) missing.Add(nameof(ReviewNotes));
        return missing;
    }

    // Saves the trade under a status, refusing it while the status's required fields are missing.
    // Marking reviewed goes through MarkReviewed, so a review always has its notes and time.
    public void SetStatus(TradeStatus status)
    {
        if (status == TradeStatus.Reviewed && Status != TradeStatus.Reviewed)
            throw new DomainException("Mark a closed trade as reviewed with its review notes.");
        RequireComplete(status);
        Status = status;
        ApplyDerivedValues();
    }

    public void MarkReviewed(string reviewNotes, DateTime now)
    {
        if (Status is not (TradeStatus.Closed or TradeStatus.Reviewed))
            throw new DomainException("Only a closed trade can be reviewed: record the exit and P&L first.");
        ReviewNotes = Text.Required(reviewNotes, MaxReviewNotesLength, "Review notes");
        ReviewedAt = now;
        Status = TradeStatus.Reviewed;
        UpdatedAt = now;
    }

    // Values calculated from what was recorded, never guessed:
    // day of week from the date, R from P&L ÷ risk, duration from entry and exit times
    public void ApplyDerivedValues()
    {
        if (TradeDate != default) DayOfWeek = TradeDate.DayOfWeek.ToString();
        if (RR is null && PnL is { } pnl && RiskAmount is > 0 and var risk) RR = Math.Round(pnl / risk, 2);
        if (TradeDuration is null && EntryTime is { } entry && ExitTime is { } exit)
        {
            var minutes = ((ExitDate ?? TradeDate).ToDateTime(exit) - TradeDate.ToDateTime(entry)).TotalMinutes;
            if (minutes >= 0) TradeDuration = (int)Math.Round(minutes);
        }
    }

    private void RequireComplete(TradeStatus status)
    {
        var missing = MissingFor(status);
        if (missing.Count > 0)
            throw new DomainException($"A {status.ToString().ToLowerInvariant()} trade needs: {string.Join(", ", missing)}.");
    }
}
