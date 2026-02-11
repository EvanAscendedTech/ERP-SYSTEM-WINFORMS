using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public class InspectionService
{
    private readonly List<string> _openFindings = new();
    private readonly List<string> _inspectionStarts = new();

    public IReadOnlyList<string> OpenFindings => _openFindings;
    public IReadOnlyList<string> InspectionStarts => _inspectionStarts;

    public bool TryStartInspection(string batchNumber, ProductionJobStatus productionStatus, string actorUserId, out string message)
    {
        if (!LifecycleWorkflowService.CanStartInspection(productionStatus, out message))
        {
            return false;
        }

        _inspectionStarts.Add($"{DateTime.UtcNow:O}|{actorUserId}|{batchNumber}");
        message = $"Inspection started for batch {batchNumber}.";
        return true;
    }

    public void AddFinding(string finding)
    {
        if (string.IsNullOrWhiteSpace(finding))
        {
            return;
        }

        _openFindings.Add(finding.Trim());
    }

    public bool CloseFinding(string finding)
    {
        return _openFindings.Remove(finding);
    }
}
