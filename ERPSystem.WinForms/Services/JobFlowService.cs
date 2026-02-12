using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public class JobFlowService
{
    private readonly HashSet<string> _qualityApproved = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inspectionPassed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _shippingCompletedUtc = new(StringComparer.OrdinalIgnoreCase);

    public bool IsQualityApproved(string jobNumber) => _qualityApproved.Contains(jobNumber);

    public bool IsInspectionPassed(string jobNumber) => _inspectionPassed.Contains(jobNumber);

    public bool IsShipped(string jobNumber) => _shippingCompletedUtc.ContainsKey(jobNumber);

    public DateTime? GetShippedUtc(string jobNumber)
        => _shippingCompletedUtc.TryGetValue(jobNumber, out var shippedUtc) ? shippedUtc : null;

    public bool TryApproveQuality(ProductionJob job, out string message)
    {
        if (job.Status != ProductionJobStatus.Completed)
        {
            message = $"Job {job.JobNumber} must be completed in Production before moving to Quality approval.";
            return false;
        }

        _qualityApproved.Add(job.JobNumber);
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
