using System.Text.Json;

namespace MelamuHoldings.Domain.Trading;

public enum TradeRevisionKind
{
    Created,
    Edited,
    Reviewed,
    ImportLinked,
    FileAdded,
    FileRemoved
}

// One value before and after a change, as the trader sees it. Null means it was empty.
public sealed record FieldChange(string Field, string Label, string? Before, string? After);

// Append-only history of a trade: one revision per save that changed something, written in the same save as the
// change. There is no update path; revisions are removed only with the trade itself.
public class TradeRevision
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public long Id { get; private set; }
    public int TradeId { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public TradeRevisionKind Kind { get; private set; }

    // The trade's status once the change was saved
    public TradeStatus Status { get; private set; }
    public string Changes { get; private set; } = "[]";

    public Trade Trade { get; private set; } = null!;

    private TradeRevision() { }

    // Takes the trade itself, so a revision of a trade not saved yet gets its id in the same save
    public TradeRevision(Trade trade, TradeRevisionKind kind, IReadOnlyList<FieldChange> changes, DateTime now)
    {
        Trade = trade;
        TradeId = trade.Id;
        Kind = kind;
        Status = trade.Status;
        Changes = JsonSerializer.Serialize(changes, Json);
        OccurredAt = now;
    }

    public IReadOnlyList<FieldChange> ChangeList => JsonSerializer.Deserialize<List<FieldChange>>(Changes, Json) ?? [];
}
