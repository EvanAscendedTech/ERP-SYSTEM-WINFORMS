using System.Collections.ObjectModel;
using System.Diagnostics;

namespace ERPSystem.WinForms.Services;

public sealed record StepParsingDiagnosticEntry(
    DateTime TimestampUtc,
    string FileName,
    string FilePath,
    long FileSizeBytes,
    bool IsSuccess,
    string ErrorCode,
    string Message,
    string StackTrace,
    string Source);

public sealed class StepParsingDiagnosticsLog
{
    private readonly object _sync = new();
    private readonly List<StepParsingDiagnosticEntry> _entries = new();

    public event EventHandler<StepParsingDiagnosticEntry>? EntryAdded;
    public event EventHandler? Cleared;

    public IReadOnlyList<StepParsingDiagnosticEntry> GetEntries()
    {
        lock (_sync)
        {
            return new ReadOnlyCollection<StepParsingDiagnosticEntry>(_entries.ToList());
        }
    }

    public void RecordAttempt(
        string? fileName,
        string? filePath,
        long fileSizeBytes,
        bool isSuccess,
        string? errorCode,
        string? message,
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
            Message: message ?? string.Empty,
            StackTrace: stackTrace ?? string.Empty,
            Source: string.IsNullOrWhiteSpace(source) ? "step-parse" : source);

        lock (_sync)
        {
            _entries.Add(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }

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
}
