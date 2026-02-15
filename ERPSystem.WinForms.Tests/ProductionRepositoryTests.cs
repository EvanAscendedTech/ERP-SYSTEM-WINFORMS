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

        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            await using var addInventory = connection.CreateCommand();
            addInventory.CommandText = @"INSERT INTO InventoryItems (Sku, Description, QuantityOnHand, ReorderThreshold, UnitOfMeasure)
                                        VALUES ('RM-001', 'Raw Material', 100, 10, 'EA');";
            await addInventory.ExecuteNonQueryAsync();
        }

        await repository.SaveJobMaterialRequirementsAsync("JOB-200", new Dictionary<string, decimal> { ["RM-001"] = 2m });

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

    [Fact]
    public async Task AssignJobToMachineAsync_PersistsMachineAndCapacitySlots()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-prod-machine-{Guid.NewGuid():N}.db");
        var repository = new ProductionRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        await repository.SaveMachineAsync(new Machine
        {
            MachineCode = "MC-01",
            Description = "CNC A",
            DailyCapacityHours = 8
        });

        await repository.SaveJobAsync(new ProductionJob
        {
            JobNumber = "JOB-300",
            ProductName = "Valve",
            PlannedQuantity = 12,
            ProducedQuantity = 0,
            DueDateUtc = DateTime.UtcNow.AddDays(2),
            Status = ProductionJobStatus.Planned,
            EstimatedDurationHours = 16
        });

        var result = await repository.AssignJobToMachineAsync("JOB-300", "MC-01", 16);
        Assert.True(result.Success);

        var schedules = await repository.GetMachineSchedulesAsync("MC-01");
        Assert.Equal(2, schedules.Count);
        Assert.All(schedules, slot => Assert.Equal("JOB-300", slot.AssignedJobNumber));
        Assert.Equal(16, schedules.Sum(slot => (int)(slot.ShiftEndUtc - slot.ShiftStartUtc).TotalHours));

        File.Delete(dbPath);
    }


    [Fact]
    public async Task SaveJobAsync_RequiresMaterialsBeforeMovingBeyondPlanned()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-prod-materials-{Guid.NewGuid():N}.db");
        var repository = new ProductionRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveJobAsync(new ProductionJob
        {
            JobNumber = "JOB-400",
            ProductName = "Gear",
            PlannedQuantity = 5,
            ProducedQuantity = 0,
            DueDateUtc = DateTime.UtcNow.AddDays(1),
            Status = ProductionJobStatus.InProgress
        }));

        File.Delete(dbPath);
    }

    [Fact]
    public async Task RunReferentialIntegrityBatchAsync_ReturnsSuccessForHealthyData()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-prod-integrity-{Guid.NewGuid():N}.db");
        var repository = new ProductionRepository(dbPath);
        await repository.InitializeDatabaseAsync();

        await repository.SaveMachineAsync(new Machine { MachineCode = "MC-INT", Description = "Integrity", DailyCapacityHours = 8 });
        await repository.SaveJobAsync(new ProductionJob
        {
            JobNumber = "JOB-401",
            ProductName = "Block",
            PlannedQuantity = 5,
            ProducedQuantity = 0,
            DueDateUtc = DateTime.UtcNow.AddDays(2),
            Status = ProductionJobStatus.Planned
        });

        var report = await repository.RunReferentialIntegrityBatchAsync();

        Assert.True(report.Success);
        Assert.Empty(report.Issues);

        File.Delete(dbPath);
    }

}
