namespace MelamuHoldings.Domain.Trading;

public class Strategy
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
}
