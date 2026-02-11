using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Tests;

public class SectionServiceTests
{
    [Fact]
    public void InspectionService_AddAndCloseFinding_Works()
    {
        var service = new InspectionService();
        service.AddFinding("Surface scratch");

        Assert.Single(service.OpenFindings);
        Assert.True(service.CloseFinding("Surface scratch"));
        Assert.Empty(service.OpenFindings);
    }

    [Fact]
    public void ArchiveService_Archive_AddsTimestampedEntry()
    {
        var service = new ArchiveService();
        service.Archive("Batch-445 report");

        var entry = Assert.Single(service.ArchivedItems);
        Assert.Contains("Batch-445 report", entry);
    }

    [Fact]
    public void QuoteWorkflowService_AllowsExpectedTransitions()
    {
        Assert.True(QuoteWorkflowService.IsTransitionAllowed(QuoteStatus.InProgress, QuoteStatus.Expired));
        Assert.False(QuoteWorkflowService.IsTransitionAllowed(QuoteStatus.Expired, QuoteStatus.Lost));
        Assert.False(QuoteWorkflowService.IsTransitionAllowed(QuoteStatus.Won, QuoteStatus.InProgress));
    }

    [Fact]
    public void LifecycleWorkflowService_RejectsCrossSectionJump_FromInProgressQuoteToArchive()
    {
        var service = new ArchiveService();

        var archived = service.TryArchiveQuote("Q-10021", QuoteStatus.InProgress, "qa.user", out var message);

        Assert.False(archived);
        Assert.Contains("only allowed for terminal records", message.ToLowerInvariant());
        Assert.Empty(service.ArchivedItems);
    }

    [Fact]
    public void InspectionService_RequiresCompletedProductionBatch()
    {
        var service = new InspectionService();

        var started = service.TryStartInspection("JOB-201", ProductionJobStatus.InProgress, "qa.user", out var message);

        Assert.False(started);
        Assert.Contains("completed production batches", message.ToLowerInvariant());
        Assert.Empty(service.InspectionStarts);
    }
}
