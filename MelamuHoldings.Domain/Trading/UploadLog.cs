using MelamuHoldings.Domain.Identity;

namespace MelamuHoldings.Domain.Trading;

public class UploadLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ParsedRows { get; set; }
    public int FailedRows { get; set; }

    // Rows not imported: already imported before, or matching a trade journaled by hand (awaiting a decision)
    public int DuplicateRows { get; set; }
    public int MatchedRows { get; set; }
    public UploadStatus Status { get; set; }
    public DateTime UploadedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
}
