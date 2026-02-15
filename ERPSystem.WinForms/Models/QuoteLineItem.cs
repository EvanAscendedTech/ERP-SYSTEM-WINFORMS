namespace ERPSystem.WinForms.Models;

public enum QuoteBlobType
{
    Technical = 0,
    MaterialPricing = 1,
    PostOpPricing = 2,
    PurchaseDocumentation = 3,
    ThreeDModel = 4,
    ToolingDocumentation = 5
}

public class QuoteBlobAttachment
{
    public int Id { get; set; }
    public int LineItemId { get; set; }
    public int QuoteId { get; set; }
    public string LifecycleId { get; set; } = string.Empty;
    public QuoteBlobType BlobType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public byte[] Sha256 { get; set; } = Array.Empty<byte>();
    public string UploadedBy { get; set; } = string.Empty;
    public string StorageRelativePath { get; set; } = string.Empty;
    public byte[] BlobData { get; set; } = Array.Empty<byte>();
    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
}

public class QuoteLineItem
{
    public int Id { get; set; }
    public int QuoteId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string DrawingNumber { get; set; } = string.Empty;
    public string DrawingName { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ProductionHours { get; set; }
    public decimal SetupHours { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal ToolingCost { get; set; }
    public decimal SecondaryOperationsCost { get; set; }
    public decimal LineItemTotal { get; set; }
    public int LeadTimeDays { get; set; }
    public bool RequiresGForce { get; set; }
    public bool RequiresSecondaryProcessing { get; set; }
    public bool RequiresPlating { get; set; }
    public bool RequiresDfars { get; set; }
    public bool RequiresMaterialTestReport { get; set; }
    public bool RequiresCertificateOfConformance { get; set; }
    public bool RequiresSecondaryOperations { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<string> AssociatedFiles { get; set; } = new();
    public List<QuoteBlobAttachment> BlobAttachments { get; set; } = new();
}
