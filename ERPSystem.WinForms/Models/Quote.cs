namespace ERPSystem.WinForms.Models;

public class Quote
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; } = QuoteStatus.InProgress;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? WonUtc { get; set; }
    public string? WonByUserId { get; set; }
    public DateTime? LostUtc { get; set; }
    public string? LostByUserId { get; set; }
    public DateTime? ExpiredUtc { get; set; }
    public string? ExpiredByUserId { get; set; }
    public List<QuoteLineItem> LineItems { get; set; } = new();
}
