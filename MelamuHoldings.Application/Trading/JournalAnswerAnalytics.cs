using System.Text.Json;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Trading.Journal;

namespace MelamuHoldings.Application.Trading;

// One answer's trades and how they turned out. Value is the filter value for the same trades (null for "not answered").
public record AnswerOutcomeDto(string? Value, string Label, AdherenceGroupDto Results);

public record AnswerBreakdownDto(
    string FieldId, string Label, JournalFieldType Type, int CompletedTrades, IReadOnlyList<AnswerOutcomeDto> Answers,
    AnswerOutcomeDto NotAnswered, bool CountsTradesMoreThanOnce, int MinimumSample);

// Results by journal answer ("how did trades go when I was Calm, Frustrated…?"). The trades are the analytics trades
// (the same filter, closed and reviewed only), and each answer row holds exactly the trades the answer filter would keep,
// so a row and its filtered view always agree. Describes what happened; it does not say the answer caused the result.
public class JournalAnswerAnalytics
{
    private readonly AnalyticsScope _scope;
    private readonly IJournalRepository _journal;

    public JournalAnswerAnalytics(AnalyticsScope scope, IJournalRepository journal)
    {
        _scope = scope;
        _journal = journal;
    }

    public async Task<AnswerBreakdownDto> ByAnswerAsync(int userId, string fieldId, AnalyticsFilter? filter)
    {
        // Only questions with fixed answers can be grouped; their answers are the ones the filter offers
        var field = (await _scope.OptionsAsync(userId)).JournalFields.FirstOrDefault(f => f.Id == fieldId.Trim())
            ?? throw new NotFoundException($"There is no journal question '{fieldId}' with fixed answers to group by.");

        var trades = (await _scope.TradesAsync(userId, filter)).Where(t => t.CountsInAnalytics && t.PnL is not null).ToList();
        var entries = trades.Count == 0 ? [] : (await _journal.GetEntriesAsync(trades.Select(t => t.Id).ToList())).ToDictionary(e => e.TradeId);
        var answered = trades.Select(t => (Trade: t, Answer: entries.TryGetValue(t.Id, out var entry)
                && entry.AnswerValues.TryGetValue(field.Id, out var value) && TradeValues.Answer(value) is not null ? value : (JsonElement?)null))
            .ToList();

        var checklist = field.Type == JournalFieldType.Checklist;
        var answers = field.Options.Select(o => new AnswerOutcomeDto(o.Value, checklist ? $"{o.Label} (ticked)" : o.Label, AdherenceService.Results(
                answered.Where(a => a.Answer is { } v && AnalyticsScope.AnswerMatches(v, o.Value)).Select(a => a.Trade).ToList())))
            .ToList();
        var notAnswered = new AnswerOutcomeDto(null, "Not answered", AdherenceService.Results(answered.Where(a => a.Answer is null).Select(a => a.Trade).ToList()));

        return new AnswerBreakdownDto(field.Id, field.Label, field.Type, trades.Count, answers, notAnswered,
            CountsTradesMoreThanOnce: field.Type is JournalFieldType.MultiSelect or JournalFieldType.Checklist,
            MinimumSample: AdherenceService.MinimumSample);
    }
}
