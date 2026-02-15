using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace ERPSystem.WinForms.Data;

public class ProductionRepository
{
    private readonly string _connectionString;
    private readonly RealtimeDataService? _realtimeDataService;

    public ProductionRepository(string dbPath, RealtimeDataService? realtimeDataService = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, ForeignKeys = true }.ToString();
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
                PlannedQuantity INTEGER NOT NULL CHECK(PlannedQuantity > 0),
                ProducedQuantity INTEGER NOT NULL CHECK(ProducedQuantity >= 0),
                DueDateUtc TEXT NOT NULL,
                Status INTEGER NOT NULL,
                SourceQuoteId INTEGER NULL,
                QuoteLifecycleId TEXT NOT NULL DEFAULT '',
                StartedUtc TEXT NULL,
                StartedByUserId TEXT NULL,
                CompletedUtc TEXT NULL,
                CompletedByUserId TEXT NULL,
                EstimatedDurationHours INTEGER NOT NULL DEFAULT 8 CHECK(EstimatedDurationHours > 0),
                FOREIGN KEY(SourceQuoteId) REFERENCES Quotes(Id) ON UPDATE CASCADE ON DELETE SET NULL,
                FOREIGN KEY(StartedByUserId) REFERENCES Users(Username) ON UPDATE CASCADE ON DELETE SET NULL,
                FOREIGN KEY(CompletedByUserId) REFERENCES Users(Username) ON UPDATE CASCADE ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS MachineSchedules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MachineCode TEXT NOT NULL,
                AssignedJobNumber TEXT NOT NULL,
                ShiftStartUtc TEXT NOT NULL,
                ShiftEndUtc TEXT NOT NULL,
                IsMaintenanceWindow INTEGER NOT NULL,
                UNIQUE(MachineCode, AssignedJobNumber, ShiftStartUtc, ShiftEndUtc),
                FOREIGN KEY(MachineCode) REFERENCES Machines(MachineCode) ON UPDATE CASCADE ON DELETE CASCADE,
                FOREIGN KEY(AssignedJobNumber) REFERENCES ProductionJobs(JobNumber) ON UPDATE CASCADE ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Machines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MachineCode TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL,
                DailyCapacityHours INTEGER NOT NULL DEFAULT 8,
                MachineType TEXT NOT NULL DEFAULT 'Other'
            );

            CREATE TABLE IF NOT EXISTS JobMachineAssignments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobNumber TEXT NOT NULL UNIQUE,
                MachineCode TEXT NOT NULL,
                AssignedUtc TEXT NOT NULL,
                StartUtc TEXT NOT NULL,
                EndUtc TEXT NOT NULL,
                DurationHours INTEGER NOT NULL CHECK(DurationHours > 0),
                FOREIGN KEY(JobNumber) REFERENCES ProductionJobs(JobNumber) ON UPDATE CASCADE ON DELETE CASCADE,
                FOREIGN KEY(MachineCode) REFERENCES Machines(MachineCode) ON UPDATE CASCADE ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS InventoryItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Sku TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL,
                QuantityOnHand REAL NOT NULL,
                ReorderThreshold REAL NOT NULL,
                UnitOfMeasure TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS JobMaterialRequirements (
                JobNumber TEXT NOT NULL,
                Sku TEXT NOT NULL,
                RequiredQuantity REAL NOT NULL CHECK(RequiredQuantity > 0),
                PRIMARY KEY(JobNumber, Sku),
                FOREIGN KEY(JobNumber) REFERENCES ProductionJobs(JobNumber) ON UPDATE CASCADE ON DELETE CASCADE,
                FOREIGN KEY(Sku) REFERENCES InventoryItems(Sku) ON UPDATE CASCADE ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS RelationshipChangeLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TableName TEXT NOT NULL,
                ChangeType TEXT NOT NULL,
                RelationshipKey TEXT NOT NULL,
                Details TEXT NOT NULL,
                ChangedUtc TEXT NOT NULL
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
        await EnsureColumnExistsAsync(connection, "ProductionJobs", "EstimatedDurationHours", "INTEGER NOT NULL DEFAULT 8");
        await EnsureColumnExistsAsync(connection, "Machines", "MachineType", "TEXT NOT NULL DEFAULT 'Other'");

        await EnsureIndexesAsync(connection);
        await EnsureTriggersAsync(connection);
    }

    public async Task<int> SaveJobAsync(ProductionJob job)
    {
        ValidateJobForPersistence(job);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await EnsureRequiredMaterialsAsync(connection, job);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProductionJobs (JobNumber, ProductName, PlannedQuantity, ProducedQuantity, DueDateUtc, Status, SourceQuoteId, QuoteLifecycleId, StartedUtc, StartedByUserId, CompletedUtc, CompletedByUserId, EstimatedDurationHours)
            VALUES ($jobNumber, $productName, $plannedQty, $producedQty, $dueDateUtc, $status, $sourceQuoteId, $quoteLifecycleId, $startedUtc, $startedByUserId, $completedUtc, $completedByUserId, $estimatedDurationHours)
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
                CompletedByUserId = excluded.CompletedByUserId,
                EstimatedDurationHours = excluded.EstimatedDurationHours;

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
        command.Parameters.AddWithValue("$estimatedDurationHours", Math.Clamp(job.EstimatedDurationHours, 1, 24 * 14));

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("ProductionJobs", "save");
        }

        return id;
    }

    public async Task SaveJobMaterialRequirementsAsync(string jobNumber, IReadOnlyDictionary<string, decimal> requirements)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM JobMaterialRequirements WHERE JobNumber = $jobNumber";
        delete.Parameters.AddWithValue("$jobNumber", jobNumber.Trim());
        await delete.ExecuteNonQueryAsync();

        foreach (var requirement in requirements)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"
                INSERT INTO JobMaterialRequirements (JobNumber, Sku, RequiredQuantity)
                VALUES ($jobNumber, $sku, $qty);";
            insert.Parameters.AddWithValue("$jobNumber", jobNumber.Trim());
            insert.Parameters.AddWithValue("$sku", requirement.Key.Trim());
            insert.Parameters.AddWithValue("$qty", Math.Max(0.01m, requirement.Value));
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<IntegrityValidationReport> RunReferentialIntegrityBatchAsync()
    {
        var report = new IntegrityValidationReport { ExecutedUtc = DateTime.UtcNow, Success = true };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var checks = new (string Name, string Sql, string Details)[]
        {
            (
                "MachineSchedulesOrphans",
                @"SELECT COUNT(1)
                  FROM MachineSchedules s
                  LEFT JOIN ProductionJobs j ON j.JobNumber = s.AssignedJobNumber
                  LEFT JOIN Machines m ON m.MachineCode = s.MachineCode
                  WHERE j.Id IS NULL OR m.Id IS NULL;",
                "Machine schedule rows must reference both a valid machine and job."
            ),
            (
                "JobMachineAssignmentsOrphans",
                @"SELECT COUNT(1)
                  FROM JobMachineAssignments a
                  LEFT JOIN ProductionJobs j ON j.JobNumber = a.JobNumber
                  LEFT JOIN Machines m ON m.MachineCode = a.MachineCode
                  WHERE j.Id IS NULL OR m.Id IS NULL;",
                "Machine assignments must reference existing jobs and machines."
            ),
            (
                "QuoteReferencesOrphans",
                @"SELECT COUNT(1)
                  FROM ProductionJobs pj
                  LEFT JOIN Quotes q ON q.Id = pj.SourceQuoteId
                  WHERE pj.SourceQuoteId IS NOT NULL AND q.Id IS NULL;",
                "Production jobs with quote references must point to a valid quote."
            )
        };

        foreach (var check in checks)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = check.Sql;
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (count > 0)
            {
                report.Success = false;
                report.Issues.Add(new IntegrityValidationIssue
                {
                    CheckName = check.Name,
                    AffectedRows = count,
                    Details = check.Details
                });
            }
        }

        return report;
    }

    public async Task<IReadOnlyList<ProductionJob>> GetJobsAsync()
    {
        var jobs = new List<ProductionJob>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT Id, JobNumber, ProductName, PlannedQuantity, ProducedQuantity, DueDateUtc, Status,
                                       SourceQuoteId, QuoteLifecycleId, StartedUtc, StartedByUserId, CompletedUtc, CompletedByUserId, EstimatedDurationHours
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
                CompletedByUserId = reader.IsDBNull(12) ? null : reader.GetString(12),
                EstimatedDurationHours = reader.IsDBNull(13) ? 8 : reader.GetInt32(13)
            });
        }

        return jobs;
    }

    public async Task<IReadOnlyList<Machine>> GetMachinesAsync()
    {
        var machines = new List<Machine>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, MachineCode, Description, DailyCapacityHours, MachineType FROM Machines ORDER BY MachineCode";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            machines.Add(new Machine
            {
                Id = reader.GetInt32(0),
                MachineCode = reader.GetString(1),
                Description = reader.GetString(2),
                DailyCapacityHours = reader.GetInt32(3),
                MachineType = reader.IsDBNull(4) ? "Other" : reader.GetString(4)
            });
        }

        return machines;
    }

    public async Task SaveMachineAsync(Machine machine)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Machines (MachineCode, Description, DailyCapacityHours, MachineType)
            VALUES ($code, $description, $capacity, $machineType)
            ON CONFLICT(MachineCode) DO UPDATE SET
                Description = excluded.Description,
                DailyCapacityHours = excluded.DailyCapacityHours,
                MachineType = excluded.MachineType;";

        command.Parameters.AddWithValue("$code", machine.MachineCode.Trim());
        command.Parameters.AddWithValue("$description", machine.Description.Trim());
        command.Parameters.AddWithValue("$capacity", Math.Clamp(machine.DailyCapacityHours, 0, 24));
        command.Parameters.AddWithValue("$machineType", string.IsNullOrWhiteSpace(machine.MachineType) ? "Other" : machine.MachineType.Trim());
        await command.ExecuteNonQueryAsync();

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("Machines", "save");
        }
    }

    public async Task<IReadOnlyList<MachineSchedule>> GetMachineSchedulesAsync(string machineCode)
    {
        var schedules = new List<MachineSchedule>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, MachineCode, AssignedJobNumber, ShiftStartUtc, ShiftEndUtc, IsMaintenanceWindow
            FROM MachineSchedules
            WHERE MachineCode = $machineCode
            ORDER BY ShiftStartUtc;";
        command.Parameters.AddWithValue("$machineCode", machineCode);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            schedules.Add(new MachineSchedule
            {
                Id = reader.GetInt32(0),
                MachineCode = reader.GetString(1),
                AssignedJobNumber = reader.GetString(2),
                ShiftStartUtc = DateTime.Parse(reader.GetString(3)),
                ShiftEndUtc = DateTime.Parse(reader.GetString(4)),
                IsMaintenanceWindow = reader.GetInt32(5) == 1
            });
        }

        return schedules;
    }

    public async Task<string> GenerateNextMachineCodeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT MachineCode
            FROM Machines
            WHERE MachineCode LIKE 'MI-%';";

        var usedNumbers = new HashSet<int>();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var code = reader.GetString(0);
            if (!code.StartsWith("MI-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var suffix = code[3..];
            if (suffix.Length == 6 && int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0)
            {
                usedNumbers.Add(number);
            }
        }

        var next = 1;
        while (usedNumbers.Contains(next))
        {
            next++;
        }

        return $"MI-{next:D6}";
    }

    public async Task<(bool Success, string Message)> AssignJobToMachineAsync(string jobNumber, string machineCode, int durationHours)
    {
        var machines = await GetMachinesAsync();
        var machine = machines.FirstOrDefault(x => string.Equals(x.MachineCode, machineCode, StringComparison.OrdinalIgnoreCase));
        if (machine is null)
        {
            return (false, $"Machine {machineCode} was not found.");
        }

        if (machine.DailyCapacityHours <= 0)
        {
            return (false, $"Machine {machineCode} has no daily capacity configured.");
        }

        var jobs = await GetJobsAsync();
        var job = jobs.FirstOrDefault(x => string.Equals(x.JobNumber, jobNumber, StringComparison.OrdinalIgnoreCase));
        if (job is null)
        {
            return (false, $"Production job {jobNumber} was not found.");
        }

        var remainingHours = Math.Max(1, durationHours);
        var scheduleRows = new List<(DateTime startUtc, DateTime endUtc)>();
        var day = DateTime.UtcNow.Date;
        var safetyCounter = 0;

        var existingMachineSlots = (await GetMachineSchedulesAsync(machine.MachineCode)).ToList();

        while (remainingHours > 0 && safetyCounter < 365)
        {
            safetyCounter++;
            var dayStart = day;
            var dayEnd = day.AddDays(1);

            var usedHours = existingMachineSlots
                .Where(slot => slot.ShiftStartUtc < dayEnd && slot.ShiftEndUtc > dayStart)
                .Sum(slot => (slot.ShiftEndUtc - slot.ShiftStartUtc).TotalHours);

            var availableHours = Math.Max(0, machine.DailyCapacityHours - (int)Math.Round(usedHours));
            if (availableHours > 0)
            {
                var startOffset = Math.Min(machine.DailyCapacityHours, (int)Math.Round(usedHours));
                var alloc = Math.Min(availableHours, remainingHours);
                var startUtc = dayStart.AddHours(startOffset);
                var endUtc = startUtc.AddHours(alloc);

                scheduleRows.Add((startUtc, endUtc));
                existingMachineSlots.Add(new MachineSchedule
                {
                    MachineCode = machine.MachineCode,
                    AssignedJobNumber = jobNumber,
                    ShiftStartUtc = startUtc,
                    ShiftEndUtc = endUtc,
                    IsMaintenanceWindow = false
                });

                remainingHours -= alloc;
            }

            day = day.AddDays(1);
        }

        if (remainingHours > 0 || scheduleRows.Count == 0)
        {
            return (false, $"Unable to allocate capacity for {jobNumber} on {machineCode}.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        async Task ExecuteAsync(string sql, params (string name, object? value)[] parameters)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            foreach (var parameter in parameters)
            {
                cmd.Parameters.AddWithValue(parameter.name, parameter.value ?? DBNull.Value);
            }

            await cmd.ExecuteNonQueryAsync();
        }

        await ExecuteAsync("DELETE FROM MachineSchedules WHERE AssignedJobNumber = $jobNumber", ("$jobNumber", jobNumber));
        await ExecuteAsync("DELETE FROM JobMachineAssignments WHERE JobNumber = $jobNumber", ("$jobNumber", jobNumber));

        foreach (var slot in scheduleRows)
        {
            await ExecuteAsync(@"
                INSERT INTO MachineSchedules (MachineCode, AssignedJobNumber, ShiftStartUtc, ShiftEndUtc, IsMaintenanceWindow)
                VALUES ($machineCode, $jobNumber, $startUtc, $endUtc, 0)",
                ("$machineCode", machine.MachineCode),
                ("$jobNumber", jobNumber),
                ("$startUtc", slot.startUtc.ToString("O")),
                ("$endUtc", slot.endUtc.ToString("O")));
        }

        await ExecuteAsync(@"
            INSERT INTO JobMachineAssignments (JobNumber, MachineCode, AssignedUtc, StartUtc, EndUtc, DurationHours)
            VALUES ($jobNumber, $machineCode, $assignedUtc, $startUtc, $endUtc, $durationHours)",
            ("$jobNumber", jobNumber),
            ("$machineCode", machine.MachineCode),
            ("$assignedUtc", DateTime.UtcNow.ToString("O")),
            ("$startUtc", scheduleRows.Min(x => x.startUtc).ToString("O")),
            ("$endUtc", scheduleRows.Max(x => x.endUtc).ToString("O")),
            ("$durationHours", durationHours));

        await transaction.CommitAsync();

        if (_realtimeDataService is not null)
        {
            await _realtimeDataService.PublishChangeAsync("MachineSchedules", "save");
        }

        return (true, $"Assigned {jobNumber} to {machineCode} for {durationHours} hour(s).");
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

    private static void ValidateJobForPersistence(ProductionJob job)
    {
        if (string.IsNullOrWhiteSpace(job.JobNumber))
        {
            throw new InvalidOperationException("A production job must include a job number.");
        }

        if (string.IsNullOrWhiteSpace(job.ProductName))
        {
            throw new InvalidOperationException("A production job must include a product name.");
        }

        if (job.PlannedQuantity <= 0)
        {
            throw new InvalidOperationException("Planned quantity must be greater than zero.");
        }

        if (job.ProducedQuantity < 0)
        {
            throw new InvalidOperationException("Produced quantity cannot be negative.");
        }
    }

    private static async Task EnsureRequiredMaterialsAsync(SqliteConnection connection, ProductionJob job)
    {
        if (job.Status == ProductionJobStatus.Planned)
        {
            return;
        }

        await using var requirementCountCommand = connection.CreateCommand();
        requirementCountCommand.CommandText = "SELECT COUNT(1) FROM JobMaterialRequirements WHERE JobNumber = $jobNumber";
        requirementCountCommand.Parameters.AddWithValue("$jobNumber", job.JobNumber.Trim());
        var requirementCount = Convert.ToInt32(await requirementCountCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        if (requirementCount == 0)
        {
            throw new InvalidOperationException($"Job {job.JobNumber} cannot move past Planned status without required materials.");
        }
    }

    private static async Task EnsureIndexesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE INDEX IF NOT EXISTS IX_MachineSchedules_Job ON MachineSchedules(AssignedJobNumber);
            CREATE INDEX IF NOT EXISTS IX_MachineSchedules_Machine ON MachineSchedules(MachineCode);
            CREATE INDEX IF NOT EXISTS IX_JobMaterialRequirements_Sku ON JobMaterialRequirements(Sku);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTriggersAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TRIGGER IF NOT EXISTS trg_ProductionJobs_StatusChange
            AFTER UPDATE OF Status ON ProductionJobs
            FOR EACH ROW
            WHEN OLD.Status <> NEW.Status
            BEGIN
                INSERT INTO RelationshipChangeLog (TableName, ChangeType, RelationshipKey, Details, ChangedUtc)
                VALUES ('ProductionJobs', 'status_change', NEW.JobNumber,
                        'Status changed from ' || OLD.Status || ' to ' || NEW.Status,
                        strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            END;

            CREATE TRIGGER IF NOT EXISTS trg_JobMachineAssignments_Insert
            AFTER INSERT ON JobMachineAssignments
            FOR EACH ROW
            BEGIN
                INSERT INTO RelationshipChangeLog (TableName, ChangeType, RelationshipKey, Details, ChangedUtc)
                VALUES ('JobMachineAssignments', 'assignment', NEW.JobNumber,
                        'Assigned to machine ' || NEW.MachineCode,
                        strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            END;";
        await command.ExecuteNonQueryAsync();
    }
}
