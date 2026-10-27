using MelamuHoldings.Domain.Identity;

namespace MelamuHoldings.Domain.Trading;

public class BehaviourAlert
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;

    // Comma-separated list of Trade IDs that triggered this alert
    public string TradeIds { get; set; } = string.Empty;

    public DateOnly DetectedOnDate { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
}
