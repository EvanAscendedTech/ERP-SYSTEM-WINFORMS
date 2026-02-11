using ERPSystem.WinForms.Data;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Tests;

public class ProductionRepositoryTests
{
    [Fact]
    public async Task SaveJobAsync_PersistsAndLoadsJob()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-prod-{Guid.NewGuid():N}.db");
        var repository = new ProductionRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        var job = new ProductionJob
        {
            JobNumber = "JOB-100",
            ProductName = "Hydraulic Pump",
            PlannedQuantity = 50,
            ProducedQuantity = 10,
            DueDateUtc = DateTime.UtcNow.AddDays(2),
            Status = ProductionJobStatus.InProgress
        };

        await repository.SaveJobAsync(job);
        var jobs = await repository.GetJobsAsync();

        var loaded = Assert.Single(jobs);
        Assert.Equal(job.JobNumber, loaded.JobNumber);
        Assert.Equal(job.Status, loaded.Status);

        File.Delete(dbPath);
    }

    [Fact]
    public async Task StartJobAsync_RequiresWonQuoteAndStoresAuditFields()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-prod-start-{Guid.NewGuid():N}.db");
        var repository = new ProductionRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        await repository.SaveJobAsync(new ProductionJob
        {
            JobNumber = "JOB-200",
            ProductName = "Bracket",
            PlannedQuantity = 10,
            ProducedQuantity = 0,
            DueDateUtc = DateTime.UtcNow.AddDays(3),
            Status = ProductionJobStatus.Planned
        });

        var denied = await repository.StartJobAsync("JOB-200", QuoteStatus.InProgress, 17, "planner.user");
        Assert.False(denied.Success);

        var started = await repository.StartJobAsync("JOB-200", QuoteStatus.Won, 17, "planner.user");
        Assert.True(started.Success);

        var job = Assert.Single(await repository.GetJobsAsync());
        Assert.Equal(ProductionJobStatus.InProgress, job.Status);
        Assert.Equal(17, job.SourceQuoteId);
        Assert.Equal("planner.user", job.StartedByUserId);
        Assert.NotNull(job.StartedUtc);

        File.Delete(dbPath);
    }
}
