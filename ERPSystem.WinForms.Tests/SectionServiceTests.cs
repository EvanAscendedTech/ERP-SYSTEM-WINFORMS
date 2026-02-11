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
}
