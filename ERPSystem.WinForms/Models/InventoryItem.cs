namespace ERPSystem.WinForms.Models;

public class InventoryItem
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public decimal ReorderThreshold { get; set; }
    public string UnitOfMeasure { get; set; } = "pcs";
}
