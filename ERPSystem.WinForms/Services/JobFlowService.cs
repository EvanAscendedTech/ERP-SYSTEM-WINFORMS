using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public class JobFlowService
{
    public enum WorkflowModule
    {
        Production = 0,
        Quality = 1,
        Inspection = 2,
        Shipping = 3
    }

    private readonly HashSet<string> _qualityApproved = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inspectionPassed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _shippingCompletedUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkflowModule> _moduleByJobNumber = new(StringComparer.OrdinalIgnoreCase);

    public bool IsQualityApproved(string jobNumber) => _qualityApproved.Contains(jobNumber);

    public bool IsInspectionPassed(string jobNumber) => _inspectionPassed.Contains(jobNumber);

    public bool IsShipped(string jobNumber) => _shippingCompletedUtc.ContainsKey(jobNumber);

    public DateTime? GetShippedUtc(string jobNumber)
        => _shippingCompletedUtc.TryGetValue(jobNumber, out var shippedUtc) ? shippedUtc : null;

    public WorkflowModule GetCurrentModule(string jobNumber)
    {
        if (_moduleByJobNumber.TryGetValue(jobNumber, out var module))
        {
            return module;
        }

        if (_inspectionPassed.Contains(jobNumber))
        {
            return WorkflowModule.Shipping;
        }

        if (_qualityApproved.Contains(jobNumber))
        {
            return WorkflowModule.Inspection;
        }

        return WorkflowModule.Production;
    }

    public bool IsInModule(string jobNumber, WorkflowModule module)
    {
        return GetCurrentModule(jobNumber) == module;
    }

    public bool TryAdvanceModule(ProductionJob job, out string message)
    {
        var current = GetCurrentModule(job.JobNumber);
        if (current == WorkflowModule.Shipping)
        {
            message = $"Job {job.JobNumber} is already at the final module (Shipping).";
            return false;
        }

        var next = (WorkflowModule)((int)current + 1);
        _moduleByJobNumber[job.JobNumber] = next;
        message = $"Job {job.JobNumber} advanced to {next}.";
        return true;
    }

    public bool TryRewindModule(ProductionJob job, out string message)
    {
        var current = GetCurrentModule(job.JobNumber);
        if (current == WorkflowModule.Production)
        {
            message = $"Job {job.JobNumber} is already at the first module (Production).";
            return false;
        }

        var previous = (WorkflowModule)((int)current - 1);
        _moduleByJobNumber[job.JobNumber] = previous;
        message = $"Job {job.JobNumber} moved back to {previous}.";
        return true;
    }

    public bool TryApproveQuality(ProductionJob job, out string message)
    {
        if (job.Status != ProductionJobStatus.Completed)
        {
            message = $"Job {job.JobNumber} must be completed in Production before moving to Quality approval.";
            return false;
        }

        _qualityApproved.Add(job.JobNumber);
        _moduleByJobNumber[job.JobNumber] = WorkflowModule.Inspection;
        message = $"Quality approved for {job.JobNumber}.";
        return true;
    }

    public bool TryPassInspection(ProductionJob job, out string message)
    {
        if (!_qualityApproved.Contains(job.JobNumber))
        {
            message = $"Job {job.JobNumber} must pass Quality before inspection can be completed.";
            return false;
        }

        _inspectionPassed.Add(job.JobNumber);
        _moduleByJobNumber[job.JobNumber] = WorkflowModule.Shipping;
        message = $"Inspection passed for {job.JobNumber}.";
        return true;
    }

    public bool TryMarkShipped(ProductionJob job, out string message)
    {
        if (!_inspectionPassed.Contains(job.JobNumber))
        {
            message = $"Job {job.JobNumber} must pass Inspection before shipping.";
            return false;
        }

        _shippingCompletedUtc[job.JobNumber] = DateTime.UtcNow;
        message = $"Job {job.JobNumber} marked as shipped.";
        return true;
    }
}
