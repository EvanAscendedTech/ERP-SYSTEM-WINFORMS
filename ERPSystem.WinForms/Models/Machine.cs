namespace ERPSystem.WinForms.Models;

public class Machine
{
    public int Id { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DailyCapacityHours { get; set; } = 8;
    public string MachineType { get; set; } = "Other";
}
