using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Tests;

public class JobFlowServiceTests
{
    [Fact]
    public void TryApproveQuality_RequiresCompletedProduction()
    {
        var service = new JobFlowService();
        var job = new ProductionJob { JobNumber = "JOB-1", Status = ProductionJobStatus.InProgress };

        var approved = service.TryApproveQuality(job, out var message);

        Assert.False(approved);
        Assert.Contains("must be completed", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryPassInspection_RequiresQualityApproval()
    {
        var service = new JobFlowService();
        var job = new ProductionJob { JobNumber = "JOB-2", Status = ProductionJobStatus.Completed };

        var passed = service.TryPassInspection(job, out var message);

        Assert.False(passed);
        Assert.Contains("must pass Quality", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryMarkShipped_TracksShipTimestamp_WhenFlowComplete()
    {
        var service = new JobFlowService();
        var job = new ProductionJob { JobNumber = "JOB-3", Status = ProductionJobStatus.Completed };

        Assert.True(service.TryApproveQuality(job, out _));
        Assert.True(service.TryPassInspection(job, out _));
        Assert.True(service.TryMarkShipped(job, out _));
        Assert.NotNull(service.GetShippedUtc(job.JobNumber));
    }
}
