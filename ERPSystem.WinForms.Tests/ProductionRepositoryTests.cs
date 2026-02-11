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
}
