namespace ERPSystem.WinForms.Models;

public class ArchivedPurchasingQuote
{
    public int ArchiveId { get; set; }
    public int OriginalQuoteId { get; set; }
    public string LifecycleQuoteId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public QuoteStatus Status { get; set; }
    public string ArchivedByUserId { get; set; } = string.Empty;
    public DateTime ArchivedUtc { get; set; }
}
