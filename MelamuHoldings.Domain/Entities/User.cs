namespace MelamuHoldings.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
    public ICollection<UploadLog> UploadLogs { get; set; } = new List<UploadLog>();
    public ICollection<BehaviourAlert> BehaviourAlerts { get; set; } = new List<BehaviourAlert>();
}