using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Tests;

public class StepParsingDiagnosticsLogTests
{
    [Fact]
    public void RecordAttempt_AppendsEntryAndRaisesEvent()
    {
        var log = new StepParsingDiagnosticsLog();
        StepParsingDiagnosticEntry? raised = null;
        log.EntryAdded += (_, entry) => raised = entry;

        log.RecordAttempt(
            fileName: "part.step",
            filePath: "C:/tmp/part.step",
            fileSizeBytes: 128,
            isSuccess: false,
            errorCode: "invalid-step-header",
            failureCategory: "STEP_PREVIEW",
            message: "Header marker was not found.",
            diagnosticDetails: "schema=unknown",
            stackTrace: "trace",
            source: "quote-upload");

        var entries = log.GetEntries();
        Assert.Single(entries);
        Assert.NotNull(raised);
        Assert.Equal("part.step", entries[0].FileName);
        Assert.Equal("invalid-step-header", entries[0].ErrorCode);
        Assert.False(entries[0].IsSuccess);
    }

    [Fact]
    public void Clear_RemovesEntriesAndRaisesEvent()
    {
        var log = new StepParsingDiagnosticsLog();
        var raised = false;
        log.Cleared += (_, _) => raised = true;

        log.RecordAttempt("part.step", "path", 64, false, "invalid-step-body", "STEP_PREVIEW", "message", "detail", "trace", "quote-upload");
        log.Clear();

        Assert.Empty(log.GetEntries());
        Assert.True(raised);
    }

    [Fact]
    public void RecordAttempt_PersistsToSqliteAndReloads()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp-step-diag-{Guid.NewGuid():N}.db");
        try
        {
            var writer = new StepParsingDiagnosticsLog(dbPath);
            writer.RecordAttempt("saved.step", "dev://saved.step", 120, false, "STEP_PARSE_FAILED", "STEP_PREVIEW", "failed", "details", "trace", "step-preview");

            var reader = new StepParsingDiagnosticsLog(dbPath);
            var rows = reader.GetEntries();
            Assert.Contains(rows, x => x.FileName == "saved.step" && x.ErrorCode == "STEP_PARSE_FAILED");
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}
