namespace ERPSystem.WinForms.Models;

public sealed class IntegrityValidationIssue
{
    public string CheckName { get; set; } = string.Empty;
    public int AffectedRows { get; set; }
    public string Details { get; set; } = string.Empty;
}

public sealed class IntegrityValidationReport
{
    public DateTime ExecutedUtc { get; set; } = DateTime.UtcNow;
    public bool Success { get; set; }
    public List<IntegrityValidationIssue> Issues { get; set; } = [];

    public string Summary => Success
        ? "All referential-integrity checks passed."
        : $"{Issues.Count} integrity check(s) reported mismatches.";
}
