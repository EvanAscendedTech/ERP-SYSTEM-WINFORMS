using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Data;

public class ProductionRepository
{
    private readonly string _connectionString;
    private readonly RealtimeDataService? _realtimeDataService;

    public ProductionRepository(string dbPath, RealtimeDataService? realtimeDataService = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        _realtimeDataService = realtimeDataService;
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
                Status INTEGER NOT NULL,
                SourceQuoteId INTEGER NULL,
                QuoteLifecycleId TEXT NOT NULL DEFAULT '',
                StartedUtc TEXT NULL,
                StartedByUserId TEXT NULL,
                CompletedUtc TEXT NULL,
                CompletedByUserId TEXT NULL
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

        await EnsureColumnExistsAsync(connection, "ProductionJobs", "SourceQuoteId", "INTEGER NULL");
        await EnsureColumnExistsAsync(connection, "ProductionJobs", "QuoteLifecycleId", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(connection, "ProductionJobs", "StartedUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "ProductionJobs", "StartedByUserId", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "ProductionJobs", "CompletedUtc", "TEXT NULL");
        await EnsureColumnExistsAsync(connection, "ProductionJobs", "CompletedByUserId", "TEXT NULL");
    }

    public async Task<int> SaveJobAsync(ProductionJob job)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProductionJobs (JobNumber, ProductName, PlannedQuantity, ProducedQuantity, DueDateUtc, Status, SourceQuoteId, QuoteLifecycleId, StartedUtc, StartedByUserId, CompletedUtc, CompletedByUserId)
            VALUES ($jobNumber, $productName, $plannedQty, $producedQty, $dueDateUtc, $status, $sourceQuoteId, $quoteLifecycleId, $startedUtc, $startedByUserId, $completedUtc, $completedByUserId)
            ON CONFLICT(JobNumber) DO UPDATE SET
                ProductName = excluded.ProductName,
                PlannedQuantity = excluded.PlannedQuantity,
                ProducedQuantity = excluded.ProducedQuantity,
                DueDateUtc = excluded.DueDateUtc,
                Status = excluded.Status,
                SourceQuoteId = excluded.SourceQuoteId,
                QuoteLifecycleId = excluded.QuoteLifecycleId,
                StartedUtc = excluded.StartedUtc,
                StartedByUserId = excluded.StartedByUserId,
                CompletedUtc = excluded.CompletedUtc,
                CompletedByUserId = excluded.CompletedByUserId;

            SELECT Id FROM ProductionJobs WHERE JobNumber = $jobNumber;";
        command.Parameters.AddWithValue("$jobNumber", job.JobNumber);
        command.Parameters.AddWithValue("$productName", job.ProductName);
        command.Parameters.AddWithValue("$plannedQty", job.PlannedQuantity);
        command.Parameters.AddWithValue("$producedQty", job.ProducedQuantity);
        command.Parameters.AddWithValue("$dueDateUtc", job.DueDateUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", (int)job.Status);
        command.Parameters.AddWithValue("$sourceQuoteId", job.SourceQuoteId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$quoteLifecycleId", job.QuoteLifecycleId);
        command.Parameters.AddWithValue("$startedUtc", job.StartedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$startedByUserId", job.StartedByUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", job.CompletedUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completedByUserId", job.CompletedByUserId ?? (object)DBNull.Value);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("ProductionJobs", "save");
        }

        return id;
    }

    public async Task<IReadOnlyList<ProductionJob>> GetJobsAsync()
    {
        var jobs = new List<ProductionJob>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT Id, JobNumber, ProductName, PlannedQuantity, ProducedQuantity, DueDateUtc, Status,
                                       SourceQuoteId, QuoteLifecycleId, StartedUtc, StartedByUserId, CompletedUtc, CompletedByUserId
                                FROM ProductionJobs ORDER BY DueDateUtc";

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
                Status = (ProductionJobStatus)reader.GetInt32(6),
                SourceQuoteId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                QuoteLifecycleId = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                StartedUtc = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
                StartedByUserId = reader.IsDBNull(10) ? null : reader.GetString(10),
                CompletedUtc = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11)),
                CompletedByUserId = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }

        return jobs;
    }

    public async Task<(bool Success, string Message)> StartJobAsync(string jobNumber, QuoteStatus sourceQuoteStatus, int sourceQuoteId, string actorUserId)
    {
        if (!LifecycleWorkflowService.CanStartProduction(sourceQuoteStatus, out var message))
        {
            return (false, message);
        }

        var job = (await GetJobsAsync()).FirstOrDefault(existing => string.Equals(existing.JobNumber, jobNumber, StringComparison.OrdinalIgnoreCase));
        if (job is null)
        {
            return (false, $"Production job {jobNumber} was not found.");
        }

        job.Status = ProductionJobStatus.InProgress;
        job.SourceQuoteId = sourceQuoteId;
        job.StartedUtc = DateTime.UtcNow;
        job.StartedByUserId = actorUserId;
        await SaveJobAsync(job);
        return (true, $"Production started for {jobNumber}.");
    }

    public async Task<(bool Success, string Message)> CompleteJobAsync(string jobNumber, string actorUserId)
    {
        var job = (await GetJobsAsync()).FirstOrDefault(existing => string.Equals(existing.JobNumber, jobNumber, StringComparison.OrdinalIgnoreCase));
        if (job is null)
        {
            return (false, $"Production job {jobNumber} was not found.");
        }

        job.Status = ProductionJobStatus.Completed;
        job.CompletedUtc = DateTime.UtcNow;
        job.CompletedByUserId = actorUserId;
        await SaveJobAsync(job);
        return (true, $"Production completed for {jobNumber}.");
    }

    private static async Task EnsureColumnExistsAsync(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await pragma.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        await alter.ExecuteNonQueryAsync();
    }
}
