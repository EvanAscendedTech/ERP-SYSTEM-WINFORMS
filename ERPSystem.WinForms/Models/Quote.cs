namespace ERPSystem.WinForms.Models;

public class Quote
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; } = QuoteStatus.InProgress;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<QuoteLineItem> LineItems { get; set; } = new();
}
