namespace ERPSystem.WinForms.Models;

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
}
