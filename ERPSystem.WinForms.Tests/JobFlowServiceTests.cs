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


    [Fact]
    public void TryRequestInspection_RequiresCompletedProduction()
    {
        var service = new JobFlowService();
        var job = new ProductionJob { JobNumber = "JOB-4", Status = ProductionJobStatus.InProgress };

        var requested = service.TryRequestInspection(job, out var message);

        Assert.False(requested);
        Assert.Contains("must be completed", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryMoveToInspectionFromProduction_MovesCompletedJobToInspection()
    {
        var service = new JobFlowService();
        var job = new ProductionJob { JobNumber = "JOB-5", Status = ProductionJobStatus.Completed };

        var moved = service.TryMoveToInspectionFromProduction(job, out var message);

        Assert.True(moved);
        Assert.Contains("moved to Inspection", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JobFlowService.WorkflowModule.Inspection, service.GetCurrentModule(job.JobNumber));
    }


    [Fact]
    public void TryMoveToModule_BypassValidation_AllowsAdministratorOverride()
    {
        var service = new JobFlowService();
        var job = new ProductionJob { JobNumber = "JOB-6", Status = ProductionJobStatus.Planned };

        var moved = service.TryMoveToModule(job, JobFlowService.WorkflowModule.Inspection, bypassValidation: true, out var message);

        Assert.True(moved);
        Assert.Contains("Inspection", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JobFlowService.WorkflowModule.Inspection, service.GetCurrentModule(job.JobNumber));
    }

    [Fact]
    public void RemoveJobState_ClearsFlowState()
    {
        var service = new JobFlowService();
        var job = new ProductionJob { JobNumber = "JOB-7", Status = ProductionJobStatus.Completed };
        service.TryApproveQuality(job, out _);

        service.RemoveJobState(job.JobNumber);

        Assert.Equal(JobFlowService.WorkflowModule.Production, service.GetCurrentModule(job.JobNumber));
        Assert.False(service.IsQualityApproved(job.JobNumber));
    }
}
