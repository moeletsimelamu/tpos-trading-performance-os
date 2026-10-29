using System.Globalization;
using System.Text.Json;
using MelamuHoldings.Application.Common;
using MelamuHoldings.Domain.Trading;

namespace MelamuHoldings.Application.Trading;

public interface ITradeHistoryRepository
{
    void Add(TradeRevision revision);
    Task<List<TradeRevision>> ListAsync(int tradeId);
}

public record TradeRevisionDto(long Id, DateTime OccurredAt, string Kind, string Status, IReadOnlyList<FieldChange> Changes);

// What changed on a trade, when, and from what to what. Services stage a revision with each change and save
// both together, so a change is never saved without its record.
public class TradeHistory
{
    private readonly ITradeHistoryRepository _repository;
    private readonly ITradeRepository _trades;

    public TradeHistory(ITradeHistoryRepository repository, ITradeRepository trades)
    {
        _repository = repository;
        _trades = trades;
    }

    // Newest first
    public async Task<IReadOnlyList<TradeRevisionDto>> ListAsync(int tradeId, int userId)
    {
        if (!await _trades.ExistsAsync(tradeId, userId)) throw new NotFoundException($"Trade {tradeId} does not exist.");
        return (await _repository.ListAsync(tradeId))
            .OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id)
            .Select(r => new TradeRevisionDto(r.Id, r.OccurredAt, r.Kind.ToString(), r.Status.ToString(), r.ChangeList))
            .ToList();
    }

    // Stages a revision when something changed (a creation is always recorded); the caller saves
    public void Record(Trade trade, TradeRevisionKind kind, IReadOnlyList<FieldChange> changes, DateTime now)
    {
        if (changes.Count == 0 && kind != TradeRevisionKind.Created) return;
        _repository.Add(new TradeRevision(trade, kind, changes, now));
    }
}

// A trade's own fields as the trader sees them, captured before and after a change and compared.
// Values are written the same way every time (invariant culture, no trailing zeros), so only real changes show.
public static class TradeValues
{
    public sealed record Value(string Field, string Label, string? Text);

    public static IReadOnlyList<Value> Capture(Trade t) =>
    [
        new(nameof(Trade.Status), "Status", t.Status.ToString()),
        new(nameof(Trade.TradeDate), "Date", Date(t.TradeDate)),
        new(nameof(Trade.Instrument), "Instrument", Text(t.Instrument)),
        new(nameof(Trade.TradingAccount), "Account", Text(t.TradingAccount)),
        new(nameof(Trade.Direction), "Direction", t.Direction?.ToString()),
        new(nameof(Trade.EntryTime), "Entry time", Time(t.EntryTime)),
        new(nameof(Trade.EntryPrice), "Entry price", Number(t.EntryPrice)),
        new(nameof(Trade.StopLoss), "Stop loss", Number(t.StopLoss)),
        new(nameof(Trade.TakeProfit), "Take profit", Number(t.TakeProfit)),
        new(nameof(Trade.PositionSize), "Position size", Number(t.PositionSize)),
        new(nameof(Trade.RiskAmount), "Risk", Number(t.RiskAmount)),
        new(nameof(Trade.ExitDate), "Exit date", t.ExitDate is { } exitDate ? Date(exitDate) : null),
        new(nameof(Trade.ExitTime), "Exit time", Time(t.ExitTime)),
        new(nameof(Trade.ExitPrice), "Exit price", Number(t.ExitPrice)),
        new(nameof(Trade.PnL), "P&L", Number(t.PnL)),
        new(nameof(Trade.Fees), "Fees", Number(t.Fees)),
        new(nameof(Trade.RR), "R", Number(t.RR)),
        new(nameof(Trade.Currency), "Currency", Text(t.Currency)),
        new(nameof(Trade.Strategy), "Strategy", Text(t.Strategy?.Name)),
        new(nameof(Trade.Session), "Session", Text(t.Session)),
        new(nameof(Trade.TradeDuration), "Duration (minutes)", t.TradeDuration?.ToString(CultureInfo.InvariantCulture)),
        new(nameof(Trade.IsManualOverride), "Manual override", t.IsManualOverride ? "Yes" : "No"),
        new(nameof(Trade.DeviationNotes), "Deviation notes", Text(t.DeviationNotes)),
        new(nameof(Trade.Notes), "Notes", Text(t.Notes)),
        new(nameof(Trade.ReviewNotes), "Review", Text(t.ReviewNotes))
    ];

    public static List<FieldChange> Diff(IReadOnlyList<Value> before, IReadOnlyList<Value> after) =>
        before.Zip(after)
            .Where(p => p.First.Text != p.Second.Text)
            .Select(p => new FieldChange(p.First.Field, p.First.Label, p.First.Text, p.Second.Text))
            .ToList();

    // A journal answer as text: lists joined, yes/no for checkboxes, a checklist as each item with its answer,
    // nothing for an empty answer
    public static string? Answer(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Text(value.GetString()),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Array => value.GetArrayLength() == 0 ? null : string.Join(", ", value.EnumerateArray().Select(Answer).Where(v => v is not null)),
        JsonValueKind.Object => Joined(value.EnumerateObject().Select(p => Answer(p.Value) is { } answer ? $"{p.Name}: {answer}" : null)),
        _ => null
    };

    private static string? Joined(IEnumerable<string?> parts) =>
        string.Join("; ", parts.OfType<string>()) is { Length: > 0 } text ? text : null;

    public static string? Rule(RuleAnswer? answer) => answer switch
    {
        RuleAnswer.Met => "Met",
        RuleAnswer.NotMet => "Not met",
        RuleAnswer.NotApplicable => "Not applicable",
        _ => null
    };

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? Time(TimeOnly? value) =>
        value?.ToString(value.Value.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture);
    private static string? Number(decimal? value) => value?.ToString("0.##########", CultureInfo.InvariantCulture);
}
