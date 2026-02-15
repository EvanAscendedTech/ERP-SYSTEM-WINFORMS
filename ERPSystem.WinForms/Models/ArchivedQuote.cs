namespace ERPSystem.WinForms.Models;

public class ArchivedQuoteSummary
{
    public int ArchiveId { get; set; }
    public int OriginalQuoteId { get; set; }
    public string LifecycleQuoteId { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime? WonUtc { get; set; }
    public DateTime ArchivedUtc { get; set; }
}

public class ArchivedQuote : ArchivedQuoteSummary
{
    public string? CompletedByUserId { get; set; }
    public string? WonByUserId { get; set; }
    public string? LostByUserId { get; set; }
    public string? ExpiredByUserId { get; set; }
    public DateTime? LostUtc { get; set; }
    public DateTime? ExpiredUtc { get; set; }
    public List<QuoteLineItem> LineItems { get; set; } = new();
}
