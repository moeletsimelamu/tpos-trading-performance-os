using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using MelamuHoldings.Domain.Trading;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.Application.Trading;

// Followed: a complete checklist with every mandatory rule met. Broke: a complete checklist with one not met.
// NotScored: no checklist, or one not finished.
public enum AdherenceFilter { Followed, Broke, NotScored }

// Narrows the trades every analytics figure is calculated from. Each condition only removes trades; the
// calculations themselves never change (docs/TRADING_JOURNAL_DESIGN.md §10, J6).
public class AnalyticsFilter : IValidatableObject
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? StrategyId { get; set; }
    [MaxLength(50)] public string? Session { get; set; }
    [MaxLength(100)] public string? Account { get; set; }
    public AdherenceFilter? Adherence { get; set; }

    // A journal question (by its field id, stable across template versions) and the answer to keep
    [MaxLength(40)] public string? AnswerField { get; set; }
    [MaxLength(200)] public string? AnswerValue { get; set; }

    public bool IsEmpty => From is null && To is null && StrategyId is null && string.IsNullOrWhiteSpace(Session)
        && string.IsNullOrWhiteSpace(Account) && Adherence is null && string.IsNullOrWhiteSpace(AnswerField);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is { } from && To is { } to && from > to)
            yield return new ValidationResult("From cannot be after To.", [nameof(From), nameof(To)]);
        if (string.IsNullOrWhiteSpace(AnswerField) != string.IsNullOrWhiteSpace(AnswerValue))
            yield return new ValidationResult("Choose both a journal question and an answer.", [nameof(AnswerField), nameof(AnswerValue)]);
    }
}

public record FilterOptionDto(string Value, string Label);

public record JournalFilterFieldDto(string Id, string Label, JournalFieldType Type, IReadOnlyList<FilterOptionDto> Options);

// What the filters can be set to, from the trader's own records
public record AnalyticsFilterOptionsDto(
    IReadOnlyList<FilterOptionDto> Strategies, IReadOnlyList<string> Sessions, IReadOnlyList<string> Accounts,
    IReadOnlyList<JournalFilterFieldDto> JournalFields);

// The one place that decides which of a trader's trades analytics look at. Performance, behaviour and adherence
// all read their trades here, then apply their own unchanged calculations.
public class AnalyticsScope
{
    private static readonly JournalFieldType[] FilterableTypes =
        [JournalFieldType.SingleSelect, JournalFieldType.MultiSelect, JournalFieldType.Rating, JournalFieldType.Checkbox, JournalFieldType.Checklist];

    private readonly ITradeRepository _trades;
    private readonly IStrategyRepository _strategies;
    private readonly IJournalRepository _journal;

    public AnalyticsScope(ITradeRepository trades, IStrategyRepository strategies, IJournalRepository journal)
    {
        _trades = trades;
        _strategies = strategies;
        _journal = journal;
    }

    public async Task<List<Trade>> TradesAsync(int userId, AnalyticsFilter? filter)
    {
        var trades = (await _trades.GetByUserIdAsync(userId)).ToList();
        if (filter is null || filter.IsEmpty) return trades;

        trades = trades.Where(t =>
            (filter.From is null || t.TradeDate >= filter.From) &&
            (filter.To is null || t.TradeDate <= filter.To) &&
            (filter.StrategyId is null || t.StrategyId == filter.StrategyId) &&
            (string.IsNullOrWhiteSpace(filter.Session) || Same(t.Session, filter.Session)) &&
            (string.IsNullOrWhiteSpace(filter.Account) || Same(t.TradingAccount, filter.Account))).ToList();

        if (filter.Adherence is { } adherence && trades.Count > 0)
        {
            var checklists = await _strategies.GetChecklistsAsync(trades.Select(t => t.Id).ToList());
            trades = trades.Where(t => Matches(adherence, checklists.FirstOrDefault(c => c.TradeId == t.Id && c.StrategyId == t.StrategyId))).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.AnswerField) && trades.Count > 0)
        {
            var entries = (await _journal.GetEntriesAsync(trades.Select(t => t.Id).ToList())).ToDictionary(e => e.TradeId);
            trades = trades.Where(t => entries.TryGetValue(t.Id, out var entry)
                && entry.AnswerValues.TryGetValue(filter.AnswerField.Trim(), out var value)
                && AnswerMatches(value, filter.AnswerValue!.Trim())).ToList();
        }
        return trades;
    }

    public async Task<AnalyticsFilterOptionsDto> OptionsAsync(int userId)
    {
        var trades = (await _trades.GetByUserIdAsync(userId)).Where(t => t.CountsInAnalytics).ToList();
        var strategies = (await _strategies.GetAllForUserAsync(userId))
            .OrderBy(s => s.Status).ThenBy(s => s.Name)
            .Select(s => new FilterOptionDto(s.Id.ToString(CultureInfo.InvariantCulture), s.Name)).ToList();
        static List<string> Distinct(IEnumerable<string?> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();

        // Every question that has fixed answers, from any version of any template, so older entries stay filterable.
        // A question keeps its latest wording; options from every version are offered.
        var fields = new Dictionary<string, (JournalField Field, List<string> Options)>();
        foreach (var version in (await _journal.GetTemplatesAsync(userId)).SelectMany(t => t.Versions).OrderBy(v => v.CreatedAt).ThenBy(v => v.Id))
            foreach (var field in version.Sections.SelectMany(s => s.Fields).Where(f => FilterableTypes.Contains(f.Type)))
            {
                var options = fields.TryGetValue(field.Id, out var known) ? known.Options : [];
                options.AddRange((field.Options ?? []).Where(o => !options.Contains(o)));
                fields[field.Id] = (field, options);
            }

        var journalFields = fields.Values.Select(f => new JournalFilterFieldDto(f.Field.Id, f.Field.Label, f.Field.Type, f.Field.Type switch
        {
            JournalFieldType.Rating => Enumerable.Range(1, 5).Select(n => new FilterOptionDto(n.ToString(CultureInfo.InvariantCulture), $"{n} of 5")).ToList(),
            JournalFieldType.Checkbox => [new FilterOptionDto("true", "Yes"), new FilterOptionDto("false", "No")],
            _ => f.Options.Select(o => new FilterOptionDto(o, o)).ToList()
        })).OrderBy(f => f.Label).ToList();

        return new AnalyticsFilterOptionsDto(strategies, Distinct(trades.Select(t => t.Session)), Distinct(trades.Select(t => t.TradingAccount)), journalFields);
    }

    // A text or choice answer equals the value; a multi-select contains it; a checklist has that item ticked;
    // a rating or number equals it; a yes/no answer equals true or false
    public static bool AnswerMatches(JsonElement answer, string wanted) => answer.ValueKind switch
    {
        JsonValueKind.String => string.Equals(answer.GetString()?.Trim(), wanted, StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Number => decimal.TryParse(wanted, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && answer.GetDecimal() == n,
        JsonValueKind.True => string.Equals(wanted, "true", StringComparison.OrdinalIgnoreCase),
        JsonValueKind.False => string.Equals(wanted, "false", StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Array => answer.EnumerateArray().Any(item => AnswerMatches(item, wanted)),
        JsonValueKind.Object => answer.TryGetProperty(wanted, out var ticked) && ticked.ValueKind == JsonValueKind.True,
        _ => false
    };

    private static bool Matches(AdherenceFilter filter, TradeChecklist? checklist) => filter switch
    {
        AdherenceFilter.Followed => checklist?.IsCompliant == true,
        AdherenceFilter.Broke => checklist?.IsCompliant == false,
        _ => checklist is null || !checklist.IsComplete
    };

    private static bool Same(string? value, string wanted) => string.Equals(value?.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);
}
