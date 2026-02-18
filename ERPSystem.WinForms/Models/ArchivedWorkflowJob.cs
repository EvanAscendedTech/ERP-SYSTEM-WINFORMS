namespace ERPSystem.WinForms.Models;

public class ArchivedWorkflowJob
{
    public int ArchiveId { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PlannedQuantity { get; set; }
    public int ProducedQuantity { get; set; }
    public DateTime DueDateUtc { get; set; }
    public ProductionJobStatus Status { get; set; }
    public int? SourceQuoteId { get; set; }
    public string QuoteLifecycleId { get; set; } = string.Empty;
    public string OriginModule { get; set; } = string.Empty;
    public string ArchivedByUserId { get; set; } = string.Empty;
    public DateTime ArchivedUtc { get; set; }
}
