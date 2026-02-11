namespace ERPSystem.WinForms.Models;

public class MachineSchedule
{
    public int Id { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public string AssignedJobNumber { get; set; } = string.Empty;
    public DateTime ShiftStartUtc { get; set; }
    public DateTime ShiftEndUtc { get; set; }
    public bool IsMaintenanceWindow { get; set; }
}
