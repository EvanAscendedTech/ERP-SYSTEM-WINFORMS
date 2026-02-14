using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Services;

public class RealtimeDataService
{
    private readonly string _connectionString;

    public RealtimeDataService(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public async Task InitializeDatabaseAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS RealtimeEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Dataset TEXT NOT NULL,
                Operation TEXT NOT NULL,
                ChangedUtc TEXT NOT NULL
            );";
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> PublishChangeAsync(string dataset, string operation)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO RealtimeEvents (Dataset, Operation, ChangedUtc)
            VALUES ($dataset, $operation, $changedUtc);
            SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$dataset", dataset);
        command.Parameters.AddWithValue("$operation", operation);
        command.Parameters.AddWithValue("$changedUtc", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<long> GetLatestEventIdAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM RealtimeEvents;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
