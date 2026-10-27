namespace MelamuHoldings.Domain.Finance;

// Append-only record of a change to financial data: who did what, when, and the values involved.
// There is no update or delete path for audit events.
public class AuditEvent
{
    public long Id { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public int UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;

    // JSON snapshot of the values recorded or changed (before/after for edits)
    public string? Data { get; private set; }

    private AuditEvent() { }

    public AuditEvent(DateTime occurredAt, int userId, string action, string entityType, string entityId, string summary, string? data)
    {
        OccurredAt = occurredAt;
        UserId = userId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Summary = summary;
        Data = data;
    }
}
