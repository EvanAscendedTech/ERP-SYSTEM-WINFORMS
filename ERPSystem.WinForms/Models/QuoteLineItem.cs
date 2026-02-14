namespace ERPSystem.WinForms.Models;

public enum QuoteBlobType
{
    Technical = 0,
    MaterialPricing = 1,
    PostOpPricing = 2
}

public class QuoteBlobAttachment
{
    public int Id { get; set; }
    public int LineItemId { get; set; }
    public QuoteBlobType BlobType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] BlobData { get; set; } = Array.Empty<byte>();
    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
}

public class QuoteLineItem
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int LeadTimeDays { get; set; }
    public bool RequiresGForce { get; set; }
    public bool RequiresSecondaryProcessing { get; set; }
    public bool RequiresPlating { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<string> AssociatedFiles { get; set; } = new();
    public List<QuoteBlobAttachment> BlobAttachments { get; set; } = new();
}
