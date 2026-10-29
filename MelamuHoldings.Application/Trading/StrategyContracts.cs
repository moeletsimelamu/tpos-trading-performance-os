using System.ComponentModel.DataAnnotations;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public record StrategyRuleDto(int Id, string Text, bool IsMandatory, RuleCategory Category, int SortOrder, DateTime? RetiredAt);

// Instruments and Sessions empty = the strategy applies anywhere
public record StrategyDto(
    int Id, string Name, string? Description, StrategyStatus Status,
    IReadOnlyList<StrategyRuleDto> Rules, IReadOnlyList<StrategyRuleDto> RetiredRules,
    IReadOnlyList<string> Instruments, IReadOnlyList<string> Sessions, int? DefaultTemplateId,
    int TradeCount, DateTime CreatedAt, DateTime? UpdatedAt);

public class SaveStrategyRequest
{
    [Required, MaxLength(Strategy.MaxNameLength)] public string Name { get; set; } = string.Empty;
    [MaxLength(Strategy.MaxDescriptionLength)] public string? Description { get; set; }

    // The active rules in order. Existing rules carry their id; a rule left out is retired.
    public List<SaveRuleRequest> Rules { get; set; } = new();

    // Where the strategy is meant to be traded; empty lists mean anywhere
    public List<string> Instruments { get; set; } = new();
    public List<string> Sessions { get; set; } = new();

    // One of the trader's active journal templates, or none
    public int? DefaultTemplateId { get; set; }
}

public class SaveRuleRequest
{
    public int? Id { get; set; }
    [Required, MaxLength(StrategyRule.MaxTextLength)] public string Text { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public RuleCategory Category { get; set; } = RuleCategory.General;
}

// One answer on a trade's checklist: Met, NotMet, or NotApplicable (optional rules only)
public class RuleAnswerRequest
{
    [Required] public int? RuleId { get; set; }
    [Required] public RuleAnswer? Answer { get; set; }
}

public record ChecklistItemDto(int RuleId, string Text, bool IsMandatory, RuleCategory Category, RuleAnswer? Answer, DateTime? AnsweredAt);

// Every answer given, in order; a Sequence above 1 is a change to an earlier answer
public record ChecklistChangeDto(int RuleId, string Text, int Sequence, RuleAnswer Answer, DateTime AnsweredAt);

public record TradeChecklistDto(
    int StrategyId, string StrategyName, DateTime CreatedAt, IReadOnlyList<ChecklistItemDto> Items,
    bool IsComplete, bool? IsCompliant, IReadOnlyList<string> Violations, IReadOnlyList<ChecklistChangeDto> History);

// Results of one group of trades. InsufficientData is true below AdherenceService.MinimumSample trades.
public record AdherenceGroupDto(int Trades, int Wins, decimal NetPnL, decimal? WinRate, decimal? AverageR, bool InsufficientData);

public record RuleViolationDto(int RuleId, string StrategyName, string Text, RuleCategory Category, int TimesNotMet, int TimesAnswered);

public enum TrendPeriod { Month, Week }

// One period's adherence: Scored = trades with a complete checklist (the denominator of the rate)
public record AdherencePeriodDto(DateOnly Start, DateOnly End, int CompletedTrades, int Scored, int Compliant, decimal? AdherenceRate, bool InsufficientData);

public record AdherenceTrendDto(TrendPeriod Period, int MinimumSample, bool Truncated, IReadOnlyList<AdherencePeriodDto> Periods);

public record StrategyAdherenceDto(int StrategyId, string StrategyName, int CompletedTrades, int WithChecklist, int Compliant, decimal? AdherenceRate);

// Adherence over closed and reviewed trades. Rate = compliant ÷ trades with a complete checklist (WithChecklist);
// trades without a checklist, or with an unfinished one, are counted separately and never in the rate.
public record AdherenceReportDto(
    int CompletedTrades, int WithChecklist, int IncompleteChecklist, int WithoutChecklist,
    int Compliant, int NonCompliant, decimal? AdherenceRate, int MinimumSample,
    AdherenceGroupDto CompliantResults, AdherenceGroupDto NonCompliantResults,
    IReadOnlyList<RuleViolationDto> MostViolated, IReadOnlyList<StrategyAdherenceDto> ByStrategy);
