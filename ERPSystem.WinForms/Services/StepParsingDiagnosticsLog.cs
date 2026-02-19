using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace ERPSystem.WinForms.Services;

public sealed record StepParsingDiagnosticEntry(
    DateTime TimestampUtc,
    string FileName,
    string FilePath,
    long FileSizeBytes,
    bool IsSuccess,
    string ErrorCode,
    string FailureCategory,
    string Message,
    string DiagnosticDetails,
    string StackTrace,
    string Source);

public sealed class StepParsingDiagnosticsLog
{
    private readonly object _sync = new();
    private readonly List<StepParsingDiagnosticEntry> _entries = new();
    private readonly string? _connectionString;

    public event EventHandler<StepParsingDiagnosticEntry>? EntryAdded;
    public event EventHandler? Cleared;

    public StepParsingDiagnosticsLog(string? databasePath = null)
    {
        if (!string.IsNullOrWhiteSpace(databasePath))
        {
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                ForeignKeys = true
            }.ToString();
            EnsureDiagnosticsTable();
            LoadPersistedEntries();
        }
    }

    public IReadOnlyList<StepParsingDiagnosticEntry> GetEntries()
    {
        lock (_sync)
        {
            return new ReadOnlyCollection<StepParsingDiagnosticEntry>(_entries.OrderByDescending(e => e.TimestampUtc).ToList());
        }
    }

    public void RecordAttempt(
        string? fileName,
        string? filePath,
        long fileSizeBytes,
        bool isSuccess,
        string? errorCode,
        string? failureCategory,
        string? message,
        string? diagnosticDetails,
        string? stackTrace,
        string? source)
    {
        var entry = new StepParsingDiagnosticEntry(
            TimestampUtc: DateTime.UtcNow,
            FileName: string.IsNullOrWhiteSpace(fileName) ? "unknown" : fileName,
            FilePath: filePath ?? string.Empty,
            FileSizeBytes: Math.Max(0, fileSizeBytes),
            IsSuccess: isSuccess,
            ErrorCode: errorCode ?? string.Empty,
            FailureCategory: failureCategory ?? string.Empty,
            Message: message ?? string.Empty,
            DiagnosticDetails: diagnosticDetails ?? string.Empty,
            StackTrace: stackTrace ?? string.Empty,
            Source: string.IsNullOrWhiteSpace(source) ? "step-parse" : source);

        lock (_sync)
        {
            _entries.Insert(0, entry);
        }

        PersistEntry(entry);
        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }

        DeletePersistedEntries();
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    public static string BuildStackTrace(Exception exception)
    {
        return exception.StackTrace ?? exception.ToString();
    }

    public static string BuildCallSiteTrace()
    {
        return new StackTrace(1, true).ToString();
    }

    private void EnsureDiagnosticsTable()
    {
        if (_connectionString is null)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS StepParsingDiagnostics (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                IsSuccess INTEGER NOT NULL,
                ErrorCode TEXT NOT NULL,
                FailureCategory TEXT NOT NULL,
                Message TEXT NOT NULL,
                DiagnosticDetails TEXT NOT NULL,
                StackTrace TEXT NOT NULL,
                Source TEXT NOT NULL
            );";
        command.ExecuteNonQuery();
    }

    private void LoadPersistedEntries()
    {
        if (_connectionString is null)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT TimestampUtc, FileName, FilePath, FileSizeBytes, IsSuccess, ErrorCode, FailureCategory, Message, DiagnosticDetails, StackTrace, Source
            FROM StepParsingDiagnostics
            ORDER BY datetime(TimestampUtc) DESC, Id DESC;";

        using var reader = command.ExecuteReader();
        lock (_sync)
        {
            _entries.Clear();
            while (reader.Read())
            {
                _entries.Add(new StepParsingDiagnosticEntry(
                    TimestampUtc: DateTime.TryParse(reader.GetString(0), out var timestamp) ? timestamp : DateTime.UtcNow,
                    FileName: reader.GetString(1),
                    FilePath: reader.GetString(2),
                    FileSizeBytes: reader.GetInt64(3),
                    IsSuccess: reader.GetInt32(4) == 1,
                    ErrorCode: reader.GetString(5),
                    FailureCategory: reader.GetString(6),
                    Message: reader.GetString(7),
                    DiagnosticDetails: reader.GetString(8),
                    StackTrace: reader.GetString(9),
                    Source: reader.GetString(10)));
            }
        }
    }

    private void PersistEntry(StepParsingDiagnosticEntry entry)
    {
        if (_connectionString is null)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO StepParsingDiagnostics
            (TimestampUtc, FileName, FilePath, FileSizeBytes, IsSuccess, ErrorCode, FailureCategory, Message, DiagnosticDetails, StackTrace, Source)
            VALUES
            (@TimestampUtc, @FileName, @FilePath, @FileSizeBytes, @IsSuccess, @ErrorCode, @FailureCategory, @Message, @DiagnosticDetails, @StackTrace, @Source);";
        command.Parameters.AddWithValue("@TimestampUtc", entry.TimestampUtc.ToString("O"));
        command.Parameters.AddWithValue("@FileName", entry.FileName);
        command.Parameters.AddWithValue("@FilePath", entry.FilePath);
        command.Parameters.AddWithValue("@FileSizeBytes", entry.FileSizeBytes);
        command.Parameters.AddWithValue("@IsSuccess", entry.IsSuccess ? 1 : 0);
        command.Parameters.AddWithValue("@ErrorCode", entry.ErrorCode);
        command.Parameters.AddWithValue("@FailureCategory", entry.FailureCategory);
        command.Parameters.AddWithValue("@Message", entry.Message);
        command.Parameters.AddWithValue("@DiagnosticDetails", entry.DiagnosticDetails);
        command.Parameters.AddWithValue("@StackTrace", entry.StackTrace);
        command.Parameters.AddWithValue("@Source", entry.Source);
        command.ExecuteNonQuery();
    }

    private void DeletePersistedEntries()
    {
        if (_connectionString is null)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StepParsingDiagnostics;";
        command.ExecuteNonQuery();
    }
}
