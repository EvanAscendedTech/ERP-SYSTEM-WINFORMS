using ERPSystem.WinForms.Models;
using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Data;

public class ProductionRepository
{
    private readonly string _connectionString;

    public ProductionRepository(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            CREATE TABLE IF NOT EXISTS ProductionJobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobNumber TEXT NOT NULL UNIQUE,
                ProductName TEXT NOT NULL,
                PlannedQuantity INTEGER NOT NULL,
                ProducedQuantity INTEGER NOT NULL,
                DueDateUtc TEXT NOT NULL,
                Status INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MachineSchedules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MachineCode TEXT NOT NULL,
                AssignedJobNumber TEXT NOT NULL,
                ShiftStartUtc TEXT NOT NULL,
                ShiftEndUtc TEXT NOT NULL,
                IsMaintenanceWindow INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS InventoryItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Sku TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL,
                QuantityOnHand REAL NOT NULL,
                ReorderThreshold REAL NOT NULL,
                UnitOfMeasure TEXT NOT NULL
            );";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> SaveJobAsync(ProductionJob job)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProductionJobs (JobNumber, ProductName, PlannedQuantity, ProducedQuantity, DueDateUtc, Status)
            VALUES ($jobNumber, $productName, $plannedQty, $producedQty, $dueDateUtc, $status)
            ON CONFLICT(JobNumber) DO UPDATE SET
                ProductName = excluded.ProductName,
                PlannedQuantity = excluded.PlannedQuantity,
                ProducedQuantity = excluded.ProducedQuantity,
                DueDateUtc = excluded.DueDateUtc,
                Status = excluded.Status;

            SELECT Id FROM ProductionJobs WHERE JobNumber = $jobNumber;";
        command.Parameters.AddWithValue("$jobNumber", job.JobNumber);
        command.Parameters.AddWithValue("$productName", job.ProductName);
        command.Parameters.AddWithValue("$plannedQty", job.PlannedQuantity);
        command.Parameters.AddWithValue("$producedQty", job.ProducedQuantity);
        command.Parameters.AddWithValue("$dueDateUtc", job.DueDateUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", (int)job.Status);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<IReadOnlyList<ProductionJob>> GetJobsAsync()
    {
        var jobs = new List<ProductionJob>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, JobNumber, ProductName, PlannedQuantity, ProducedQuantity, DueDateUtc, Status FROM ProductionJobs ORDER BY DueDateUtc";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            jobs.Add(new ProductionJob
            {
                Id = reader.GetInt32(0),
                JobNumber = reader.GetString(1),
                ProductName = reader.GetString(2),
                PlannedQuantity = reader.GetInt32(3),
                ProducedQuantity = reader.GetInt32(4),
                DueDateUtc = DateTime.Parse(reader.GetString(5)),
                Status = (ProductionJobStatus)reader.GetInt32(6)
            });
        }

        return jobs;
    }
}
