namespace ERPSystem.WinForms.Models;

public enum ProductionJobStatus
{
    Planned = 0,
    InProgress = 1,
    Completed = 2,
    OnHold = 3
}

public class ProductionJob
{
    public int Id { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PlannedQuantity { get; set; }
    public int ProducedQuantity { get; set; }
    public DateTime DueDateUtc { get; set; } = DateTime.UtcNow.AddDays(1);
    public ProductionJobStatus Status { get; set; } = ProductionJobStatus.Planned;
    public int? SourceQuoteId { get; set; }
    public string QuoteLifecycleId { get; set; } = string.Empty;
    public DateTime? StartedUtc { get; set; }
    public string? StartedByUserId { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? CompletedByUserId { get; set; }
}
