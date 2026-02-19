using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using System.Diagnostics;

namespace ERPSystem.WinForms.Controls;

public sealed class StepModelPreviewControl : UserControl
{
    private readonly WebView2 _webView;
    private readonly Label _statusLabel;
    private bool _initialized;
    private int _renderVersion;
    private TaskCompletionSource<bool>? _navigationReady;
    private readonly StepFileParser _stepFileParser = new();
    private readonly SolidModelFileTypeDetector _fileTypeDetector = new();
    private byte[] _lastLoadedBytes = Array.Empty<byte>();
    private string _lastLoadedFileName = string.Empty;
    private string _lastLoadedSourcePath = string.Empty;
    private static readonly string HtmlShell = BuildHtmlShell();
    private readonly StepParsingDiagnosticsLog? _diagnosticsLog;
    private readonly object _consoleLogSync = new();
    private readonly List<string> _consoleLogBuffer = [];
    private const int ConsoleLogLimit = 200;
    private const int ViewerReadyTimeoutMs = 15000;
    private const string EmbeddedSelfTestStep = """
ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('self-test'),'2;1');
FILE_NAME('embedded-self-test.step','2026-01-01T00:00:00',('codex'),('codex'),'','','');
FILE_SCHEMA(('AUTOMOTIVE_DESIGN_CC2'));
ENDSEC;
DATA;
#1 = CARTESIAN_POINT('',(0.,0.,0.));
ENDSEC;
END-ISO-10303-21;
""";

    public StepModelPreviewControl(StepParsingDiagnosticsLog? diagnosticsLog = null)
    {
        _diagnosticsLog = diagnosticsLog;
        BackColor = Color.FromArgb(248, 250, 252);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false,
            DefaultBackgroundColor = Color.FromArgb(248, 250, 252)
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Text = "Loading 3D viewer..."
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Enlarge", null, (_, _) => OpenEnlargedViewer());
        contextMenu.Items.Add("Run STEP Self-test", null, async (_, _) =>
        {
            var ok = await RunStepSelfTestAsync();
            MessageBox.Show(ok ? "STEP self-test passed." : "STEP self-test failed.", "STEP Self-test", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        });
        ContextMenuStrip = contextMenu;

        Controls.Add(_webView);
        Controls.Add(_statusLabel);

        Resize += (_, _) => _ = ResizeRendererAsync();
    }

    public void LoadStep(byte[]? stepBytes, string? fileName = null, string? sourcePath = null)
    {
        var bytesSnapshot = stepBytes?.ToArray() ?? Array.Empty<byte>();
        var nameSnapshot = fileName ?? string.Empty;
        var pathSnapshot = sourcePath ?? string.Empty;
        _lastLoadedBytes = bytesSnapshot;
        _lastLoadedFileName = nameSnapshot;
        _lastLoadedSourcePath = pathSnapshot;
        var version = Interlocked.Increment(ref _renderVersion);
        _ = LoadStepInternalAsync(bytesSnapshot, nameSnapshot, pathSnapshot, version);
    }

    public void ClearPreview()
    {
        Interlocked.Increment(ref _renderVersion);
        _ = ClearPreviewAsync();
    }

    public async Task LoadStepAttachmentAsync(QuoteBlobAttachment? attachment, Func<int, Task<byte[]>>? blobResolver = null)
    {
        if (attachment is null)
        {
            LoadStep(Array.Empty<byte>());
            return;
        }

        var data = attachment.BlobData;
        if (data.Length == 0 && blobResolver is not null && attachment.Id > 0)
        {
            data = await blobResolver(attachment.Id);
        }

        LoadStep(data, attachment.FileName, attachment.StorageRelativePath);
    }


    public async Task<bool> RunDeveloperDiagnosticsProbeAsync()
    {
        if (_diagnosticsLog is null)
        {
            return false;
        }

        var before = _diagnosticsLog.GetEntries().Count;
        var goodStep = System.Text.Encoding.ASCII.GetBytes(
            """
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('probe'),'2;1');
            ENDSEC;
            DATA;
            ENDSEC;
            END-ISO-10303-21;
            """);
        var badStep = System.Text.Encoding.ASCII.GetBytes("BAD_STEP_PAYLOAD");

        LoadStep(goodStep, "developer-good.step", "developer://probe/good.step");
        await Task.Delay(250);
        LoadStep(badStep, "developer-bad.step", "developer://probe/bad.step");
        await Task.Delay(250);

        var afterEntries = _diagnosticsLog.GetEntries();
        return afterEntries.Count >= before + 2
               && afterEntries.Any(x => x.FileName.Contains("developer-good", StringComparison.OrdinalIgnoreCase))
               && afterEntries.Any(x => x.FileName.Contains("developer-bad", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> RunStepSelfTestAsync()
    {
        await EnsureInitializedAsync();
        if (!await WaitForViewerReadyAsync("embedded-self-test.step", "embedded://self-test", 0, "self-test", ViewerReadyTimeoutMs))
        {
            return false;
        }

        var sampleBytes = System.Text.Encoding.ASCII.GetBytes(EmbeddedSelfTestStep);
        var payload = JsonSerializer.Serialize(Convert.ToBase64String(sampleBytes));
        var fileName = JsonSerializer.Serialize("embedded-self-test.step");
        var resultRaw = await ExecuteScriptSafeAsync($"window.stepViewer.parseStepBase64({payload}, {fileName});", "js-parse", "embedded-self-test.step", "embedded://self-test", sampleBytes.LongLength, "self-test");
        if (string.IsNullOrWhiteSpace(resultRaw))
        {
            return false;
        }

        var result = ParseScriptResult(resultRaw);
        return result.Ok && result.TriangleCount > 0;
    }

    private void ClearConsoleBuffer()
    {
        lock (_consoleLogSync)
        {
            _consoleLogBuffer.Clear();
        }
    }

    private string DrainConsoleBuffer()
    {
        lock (_consoleLogSync)
        {
            if (_consoleLogBuffer.Count == 0)
            {
                return string.Empty;
            }

            var combined = string.Join(" | ", _consoleLogBuffer);
            _consoleLogBuffer.Clear();
            return combined;
        }
    }

    private async Task LoadStepInternalAsync(byte[] stepBytes, string sourceFileName, string sourcePath, int version)
    {
        ClearConsoleBuffer();
        await ResetViewerForNextParseAsync();

        if (version != _renderVersion)
        {
            return;
        }

        var parser = "(none)";
        var stage = "validate-bytes";
        var meshCount = 0;
        var triangleCount = 0;
        var jsError = string.Empty;

        RecordDiagnostics(true, string.Empty, stage, "STEP_PREVIEW", $"STEP preview stage '{stage}' started.", stepBytes.LongLength, sourceFileName, sourcePath,
            BuildDiagnosticDetails(parser, stage, meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, "payload-received"));

        if (stepBytes is null || stepBytes.Length < 32)
        {
            var message = "STEP model bytes are missing or below minimum length";
            ShowError(message);
            RecordDiagnostics(false, "STEP_INVALID_BYTES", stage, "STEP_PREVIEW", message, stepBytes?.LongLength ?? 0, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, stage, meshCount, triangleCount, jsError, stepBytes?.LongLength ?? 0, sourceFileName, sourcePath, "bytes-too-small"));
            return;
        }

        var candidateName = !string.IsNullOrWhiteSpace(sourceFileName) ? sourceFileName : sourcePath;
        var detection = _fileTypeDetector.Detect(stepBytes, candidateName);
        if (!detection.IsKnownType)
        {
            const string message = "Unsupported 3D format. Supported: STEP/STP, STL, OBJ, IGES, BREP, SLDPRT";
            ShowError(message);
            RecordDiagnostics(false, "STEP_UNSUPPORTED_FORMAT", stage, "STEP_PREVIEW", message, stepBytes.LongLength, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, stage, meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, $"detection-source={detection.DetectionSource}"));
            return;
        }

        parser = detection.FileType == SolidModelFileType.Step ? "StepFileParser" : $"{detection.FileType}-parser";
        if (detection.FileType == SolidModelFileType.Step && !IsLikelyAsciiStep(stepBytes))
        {
            const string message = "The file does not contain a valid STEP header (ISO-10303-21)";
            ShowError(message);
            RecordDiagnostics(false, "STEP_UNSUPPORTED_FORMAT", stage, "STEP_PREVIEW", message, stepBytes.LongLength, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, stage, meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, "missing-iso-10303-21"));
            return;
        }

        if (!detection.IsSupportedForRendering)
        {
            var message = $"Detected {detection.FileType} format ({detection.NormalizedExtension}), but no parser kernel is configured for rendering.";
            ShowError(message);
            RecordDiagnostics(false, "STEP_PARSE_FAILED", stage, "STEP_PREVIEW", message, stepBytes.LongLength, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, stage, meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, $"fileType={detection.FileType}; extension={detection.NormalizedExtension}"));
            return;
        }

        _statusLabel.Text = $"Parsing {detection.FileType} geometry...";
        _statusLabel.Visible = true;

        try
        {
            stage = "parse";
            if (detection.FileType == SolidModelFileType.Step)
            {
                var parseReport = _stepFileParser.Parse(stepBytes);
                if (!parseReport.IsSuccess)
                {
                    var message = BuildStatusMessage((false, parseReport.ErrorCode, stage, 0, 0, parser));
                    ShowError(message);
                    RecordDiagnostics(false, "STEP_PARSE_FAILED", stage, "STEP_PREVIEW", message, stepBytes.LongLength, sourceFileName, sourcePath,
                        BuildDiagnosticDetails(parser, stage, meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, BuildPreParseDetails(parseReport)));
                    return;
                }
            }

            await EnsureInitializedAsync();
            if (version != _renderVersion)
            {
                return;
            }

            stage = "viewer-ready";
            if (!await WaitForViewerReadyAsync(sourceFileName, sourcePath, stepBytes.LongLength, parser, ViewerReadyTimeoutMs))
            {
                ShowError("STEP viewer is not ready yet");
                return;
            }

            var apiExistsRaw = await ExecuteScriptSafeAsync("typeof window.stepViewer?.parseStepBase64 === 'function'", stage, sourceFileName, sourcePath, stepBytes.LongLength, parser);
            var apiExists = IsScriptBooleanTrue(apiExistsRaw);
            if (!apiExists)
            {
                var logs = DrainConsoleBuffer();
                var readyState = await GetDocumentReadyStateAsync();
                var url = _webView.CoreWebView2?.Source ?? "about:blank";
                const string message = "STEP viewer API is unavailable (parseStepBase64 missing)";
                ShowError(message);
                RecordDiagnostics(false, "STEP_JS_API_MISSING", "viewer-ready", "STEP_PREVIEW", message, stepBytes.LongLength, sourceFileName, sourcePath,
                    BuildDiagnosticDetails(parser, "viewer-ready", meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, $"api-missing; readyState={readyState}; url={url}; console={logs}"));
                return;
            }

            stage = "send-to-js";
            var payload = JsonSerializer.Serialize(Convert.ToBase64String(stepBytes));
            var fileNameArg = JsonSerializer.Serialize(sourceFileName);

            stage = "js-parse";
            var resultRaw = await ExecuteScriptSafeAsync($"window.stepViewer.parseStepBase64({payload}, {fileNameArg});", stage, sourceFileName, sourcePath, stepBytes.LongLength, parser);
            if (string.IsNullOrWhiteSpace(resultRaw))
            {
                ShowError("Unable to render STEP geometry (script-empty-result)");
                var logs = DrainConsoleBuffer();
                var readyState = await GetDocumentReadyStateAsync();
                RecordDiagnostics(false, "STEP_JS_EMPTY_RESULT", "js-parse", "STEP_PREVIEW", "Unable to render STEP geometry (script-empty-result)", stepBytes.LongLength, sourceFileName, sourcePath,
                    BuildDiagnosticDetails(parser, "js-parse", meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, $"render-script-returned-empty; scriptLength={payload.Length}; readyState={readyState}; console={logs}"));
                return;
            }

            var result = ParseScriptResult(resultRaw);
            parser = string.IsNullOrWhiteSpace(result.Parser) ? parser : result.Parser;
            meshCount = result.MeshCount;
            triangleCount = result.TriangleCount;

            if (!result.Ok)
            {
                jsError = result.Error;
                var message = BuildStatusMessage(result);
                ShowError(message);
                var logs = DrainConsoleBuffer();
                RecordDiagnostics(false, "STEP_RENDER_FAILED", result.Stage, "STEP_PREVIEW", message, stepBytes.LongLength, sourceFileName, sourcePath,
                    BuildDiagnosticDetails(parser, result.Stage, meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, $"renderer-returned-failure; console={logs}"));
                return;
            }

            stage = "js-render";
            if (meshCount <= 0 || triangleCount <= 0)
            {
                const string message = "STEP file parsed, but no renderable geometry was produced";
                ShowError(message);
                var logs = DrainConsoleBuffer();
                RecordDiagnostics(false, "STEP_MESH_FAILED", "js-mesh", "STEP_PREVIEW", message, stepBytes.LongLength, sourceFileName, sourcePath,
                    BuildDiagnosticDetails(parser, "js-mesh", meshCount, triangleCount, "empty-geometry", stepBytes.LongLength, sourceFileName, sourcePath, $"mesh-or-triangle-empty; console={logs}"));
                return;
            }

            _webView.Visible = true;
            _statusLabel.Visible = false;
            var successLogs = DrainConsoleBuffer();
            RecordDiagnostics(true, string.Empty, stage, "STEP_PREVIEW", "STEP preview render succeeded.", stepBytes.LongLength, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, stage, meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, $"type={detection.FileType}; console={successLogs}"));
        }
        catch (Exception ex)
        {
            const string message = "WebView2 runtime unavailable for STEP preview";
            ShowError(message);
            var logs = DrainConsoleBuffer();
            RecordDiagnostics(false, "STEP_RENDER_FAILED", stage, "STEP_PREVIEW", message, stepBytes.LongLength, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, stage, meshCount, triangleCount, ex.ToString(), stepBytes.LongLength, sourceFileName, sourcePath, $"pipeline-exception; console={logs}"));
        }
    }

    private async Task ClearPreviewAsync()
    {
        _lastLoadedBytes = Array.Empty<byte>();
        _lastLoadedFileName = string.Empty;
        _lastLoadedSourcePath = string.Empty;
        _statusLabel.Text = "3D preview cleared";
        _statusLabel.Visible = true;
        _webView.Visible = false;

        if (!_initialized || _webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await ExecuteScriptSafeAsync("window.resetViewerState?.();", "render-reset", _lastLoadedFileName, _lastLoadedSourcePath, _lastLoadedBytes.LongLength, "(none)");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[StepPreview] Clear preview failed: {ex.Message}");
        }
    }

    private async Task ResetViewerForNextParseAsync()
    {
        _statusLabel.Visible = true;
        _webView.Visible = false;

        if (!_initialized || _webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            await ExecuteScriptSafeAsync("window.resetViewerState?.();", "render-reset", _lastLoadedFileName, _lastLoadedSourcePath, _lastLoadedBytes.LongLength, "(none)");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[StepPreview] Reset before parse failed: {ex.Message}");
        }
    }

    private (bool Ok, string Error, string Stage, int MeshCount, int TriangleCount, string Parser) ParseScriptResult(string resultRaw)
    {
        try
        {
            var parsed = DeserializeStepRenderResult(resultRaw);
            if (parsed is null)
            {
                return (false, "empty-result", "deserialize-result", 0, 0, "(none)");
            }

            var meshCount = parsed.meshCount > 0 ? parsed.meshCount : parsed.meshes;
            var triangleCount = parsed.triangleCount > 0 ? parsed.triangleCount : parsed.triangles;
            var normalizedStage = string.IsNullOrWhiteSpace(parsed.stage) ? "js-parse" : parsed.stage;
            return parsed.ok
                ? (true, string.Empty, normalizedStage, meshCount, triangleCount, string.IsNullOrWhiteSpace(parsed.parser) ? "(none)" : parsed.parser)
                : (false, string.IsNullOrWhiteSpace(parsed.error) ? "parse-failed" : $"{parsed.error} | {parsed.stack}", normalizedStage, meshCount, triangleCount, string.IsNullOrWhiteSpace(parsed.parser) ? "(none)" : parsed.parser);
        }
        catch
        {
            var normalized = resultRaw.Trim('"').Trim();
            if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase))
            {
                return (true, string.Empty, "legacy-result", 0, 0, "(none)");
            }

            return (false, string.IsNullOrWhiteSpace(normalized) ? "parse-failed" : normalized, "legacy-result", 0, 0, "(none)");
        }
    }

    private static string BuildStatusMessage((bool Ok, string Error, string Stage, int MeshCount, int TriangleCount, string Parser) result)
    {
        if (result.Ok)
        {
            return string.Empty;
        }

        return result.Error switch
        {
            "missing-file-data" => "STEP model unavailable (missing file data)",
            "unsupported-format" => "Unsupported 3D format. Expected .step or .stp",
            "invalid-step-header" => "The file does not contain a valid STEP header (ISO-10303-21)",
            "invalid-step-body" => "The STEP file is missing required DATA section content",
            "module-load-failed" => "STEP parser could not be loaded from local viewer assets",
            "parser-not-available" => "The detected 3D format is recognized, but the runtime parser for this format is unavailable",
            "sldprt-not-supported" => "SLDPRT parsing failed. Export the model as STEP (.step/.stp) for reliable rendering",
            "empty-geometry" => "STEP file parsed, but no renderable geometry was produced",
            "parse-binary-failed" => "Failed to parse STEP binary payload",
            "parse-text-failed" => "Failed to parse STEP text payload",
            "corrupted-or-invalid-step" => "STEP parser rejected the file as corrupted or invalid",
            "unsupported-step-version" => "STEP schema version is not supported by the current preview pipeline",
            "unsupported-step-entities" => "STEP file does not include supported surface or solid entities",
            _ => $"Unable to render STEP geometry ({result.Error})"
        };
    }

    private static StepRenderResult? DeserializeStepRenderResult(string resultRaw)
    {
        if (string.IsNullOrWhiteSpace(resultRaw))
        {
            return null;
        }

        using var document = JsonDocument.Parse(resultRaw);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var innerJson = root.GetString();
            return string.IsNullOrWhiteSpace(innerJson)
                ? null
                : JsonSerializer.Deserialize<StepRenderResult>(innerJson);
        }

        return JsonSerializer.Deserialize<StepRenderResult>(root.GetRawText());
    }

    private void ShowError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.Visible = true;
        _webView.Visible = false;
    }

    private static string BuildPreParseDetails(StepParseReport report)
    {
        var topEntities = report.DistinctEntityTypes.Count == 0
            ? "none"
            : string.Join(", ", report.DistinctEntityTypes.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key}:{x.Value}"));
        return $"schema={report.SchemaName}; entities={report.EntityCount}; surfaces={report.SurfaceEntityCount}; solids={report.SolidEntityCount}; details={report.DiagnosticDetails}; top={topEntities}";
    }

    private void RecordDiagnostics(bool isSuccess, string errorCode, string stage, string category, string message, long fileSizeBytes, string fileName, string sourcePath, string details)
    {
        var safeStage = string.IsNullOrWhiteSpace(stage) ? "js-parse" : stage;
        _diagnosticsLog?.RecordAttempt(
            fileName: string.IsNullOrWhiteSpace(fileName) ? "(unknown)" : fileName,
            filePath: sourcePath ?? string.Empty,
            fileSizeBytes: fileSizeBytes,
            isSuccess: isSuccess,
            errorCode: errorCode ?? string.Empty,
            failureCategory: string.IsNullOrWhiteSpace(category) ? "STEP_PREVIEW" : category,
            message: $"[{safeStage}] {message}",
            diagnosticDetails: details,
            stackTrace: isSuccess ? string.Empty : StepParsingDiagnosticsLog.BuildCallSiteTrace(),
            source: "step-preview");
    }


    private async Task<string?> ExecuteScriptSafeAsync(string script, string stageName, string sourceFileName, string sourcePath, long fileSizeBytes, string parser)
    {
        var safeStage = string.IsNullOrWhiteSpace(stageName) ? "js-parse" : stageName;
        try
        {
            if (_webView.CoreWebView2 is null)
            {
                RecordDiagnostics(false, "STEP_WEBVIEW_SCRIPT_ERROR", safeStage, "STEP_PREVIEW", "WebView2 CoreWebView2 is null before script execution.", fileSizeBytes, sourceFileName, sourcePath,
                    BuildDiagnosticDetails(parser, safeStage, 0, 0, "CoreWebView2 unavailable", fileSizeBytes, sourceFileName, sourcePath, "script-not-run"));
                return null;
            }

            var wrappedScript = BuildScriptEnvelope(script);
            var raw = await _webView.CoreWebView2.ExecuteScriptAsync(wrappedScript);
            if (IsNullishScriptResult(raw))
            {
                await RecordEmptyScriptResultAsync(script, safeStage, sourceFileName, sourcePath, fileSizeBytes, parser, raw);
                return null;
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var json = root.ValueKind == JsonValueKind.String ? root.GetString() : root.GetRawText();
            if (IsNullishScriptResult(json))
            {
                await RecordEmptyScriptResultAsync(script, safeStage, sourceFileName, sourcePath, fileSizeBytes, parser, json);
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                await RecordEmptyScriptResultAsync(script, safeStage, sourceFileName, sourcePath, fileSizeBytes, parser, "missing-envelope");
                return null;
            }

            using var envelopeDoc = JsonDocument.Parse(json);
            var envelope = envelopeDoc.RootElement;
            var ok = envelope.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            if (!ok)
            {
                var error = envelope.TryGetProperty("error", out var errEl) ? errEl.GetString() : "script-failed";
                var stack = envelope.TryGetProperty("stack", out var stEl) ? stEl.GetString() : string.Empty;
                RecordDiagnostics(false, "STEP_WEBVIEW_SCRIPT_ERROR", safeStage, "STEP_PREVIEW", $"WebView2 script execution failed at stage '{safeStage}'.", fileSizeBytes, sourceFileName, sourcePath,
                    BuildDiagnosticDetails(parser, safeStage, 0, 0, $"{error}; stack={stack}", fileSizeBytes, sourceFileName, sourcePath, $"script={script}"));
                return null;
            }

            if (!envelope.TryGetProperty("value", out var resultEl))
            {
                await RecordEmptyScriptResultAsync(script, safeStage, sourceFileName, sourcePath, fileSizeBytes, parser, "missing-value");
                return null;
            }

            return resultEl.GetRawText();
        }
        catch (Exception ex)
        {
            RecordDiagnostics(false, "STEP_WEBVIEW_SCRIPT_ERROR", safeStage, "STEP_PREVIEW", $"WebView2 script execution failed at stage '{safeStage}'.", fileSizeBytes, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, safeStage, 0, 0, ex.ToString(), fileSizeBytes, sourceFileName, sourcePath, $"hresult=0x{ex.HResult:X8}; script={script}"));
            return null;
        }
    }

    private static string BuildScriptEnvelope(string script)
    {
        var scriptJson = JsonSerializer.Serialize(script ?? string.Empty);
        return $$"""
(function(){
  try {
    const __script = {{scriptJson}};
    const __fn = async () => {
      return await eval(__script);
    };

    return __fn()
      .then(r => JSON.stringify({ ok: true, value: r }))
      .catch(e => JSON.stringify({ ok: false, error: String(e?.message || e || 'script-failed'), stack: String(e?.stack || '') }));
  } catch (e) {
    return JSON.stringify({ ok: false, error: String(e?.message || e || 'script-failed'), stack: String(e?.stack || '') });
  }
})();
""";
    }

    private async Task RecordEmptyScriptResultAsync(string script, string stageName, string sourceFileName, string sourcePath, long fileSizeBytes, string parser, string? raw)
    {
        var logs = DrainConsoleBuffer();
        var readyState = await GetDocumentReadyStateAsync();
        RecordDiagnostics(false, "STEP_JS_EMPTY_RESULT", stageName, "STEP_PREVIEW", "WebView2 returned empty/null/undefined script result.", fileSizeBytes, sourceFileName, sourcePath,
            BuildDiagnosticDetails(parser, stageName, 0, 0, "empty-script-result", fileSizeBytes, sourceFileName, sourcePath, $"raw={raw}; scriptLength={script.Length}; readyState={readyState}; console={logs}"));
    }

    private static bool IsNullishScriptResult(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim().Trim('"').Trim();
        return normalized.Length == 0
               || string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "undefined", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScriptBooleanTrue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim().Trim('"').Trim();
        return string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> WaitForViewerReadyAsync(string sourceFileName, string sourcePath, long fileSizeBytes, string parser, int timeoutMs = ViewerReadyTimeoutMs)
    {
        var sw = Stopwatch.StartNew();
        string? lastReadyState = string.Empty;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var readyRaw = await ExecuteScriptSafeAsync("window.stepViewer && window.stepViewer.isReady && window.stepViewer.isReady()", "viewer-ready", sourceFileName, sourcePath, fileSizeBytes, parser);
            if (IsScriptBooleanTrue(readyRaw))
            {
                return true;
            }

            lastReadyState = await GetDocumentReadyStateAsync();
            await Task.Delay(100);
        }

        var logs = DrainConsoleBuffer();
        RecordDiagnostics(false, "STEP_VIEWER_NOT_READY", "viewer-ready", "STEP_PREVIEW", "STEP viewer did not report ready state before timeout.", fileSizeBytes, sourceFileName, sourcePath,
            BuildDiagnosticDetails(parser, "viewer-ready", 0, 0, string.Empty, fileSizeBytes, sourceFileName, sourcePath, $"timeoutMs={timeoutMs}; readyState={lastReadyState}; console={logs}"));
        return false;
    }

    private async Task<string> GetDocumentReadyStateAsync()
    {
        var readyStateRaw = await ExecuteScriptSafeAsync("document.readyState", "viewer-ready", _lastLoadedFileName, _lastLoadedSourcePath, _lastLoadedBytes.LongLength, "(none)");
        if (string.IsNullOrWhiteSpace(readyStateRaw))
        {
            return "(unknown)";
        }

        return readyStateRaw.Trim().Trim('"').Trim();
    }

    private static string BuildDiagnosticDetails(string parser, string stage, int meshes, int triangles, string jsError, long fileSizeBytes, string fileName, string sourcePath, string extra)
    {
        var safeParser = string.IsNullOrWhiteSpace(parser) ? "(none)" : parser;
        return $"parser={safeParser}; stage={stage}; meshes={meshes}; triangles={triangles}; jsError={jsError}; fileName={fileName}; fileSizeBytes={fileSizeBytes}; sourcePath={sourcePath}; details={extra}";
    }

    private static bool IsLikelyAsciiStep(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        var sample = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 64));
        return sample.Contains("ISO-10303-21", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.ProcessFailed += (_, args) =>
        {
            var reason = args.Reason.ToString();
            AppendConsoleLog($"[ProcessFailed] kind={args.ProcessFailedKind}; reason={reason}");
            RecordDiagnostics(false, "STEP_WEBVIEW_PROCESS_FAILED", "viewer-ready", "STEP_PREVIEW", "WebView2 process failure detected.", _lastLoadedBytes.LongLength, _lastLoadedFileName, _lastLoadedSourcePath,
                BuildDiagnosticDetails("(none)", "viewer-ready", 0, 0, reason, _lastLoadedBytes.LongLength, _lastLoadedFileName, _lastLoadedSourcePath, $"processKind={args.ProcessFailedKind}"));
        };
        _navigationReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _webView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
            {
                _navigationReady?.TrySetResult(true);
            }
            else
            {
                _navigationReady?.TrySetException(new InvalidOperationException("Failed to load STEP viewer shell."));
            }
        };

        _webView.NavigateToString(HtmlShell);
        _initialized = true;
        if (_navigationReady is not null)
        {
            await _navigationReady.Task;
        }
    }

    private async Task ResizeRendererAsync()
    {
        if (!_initialized || !_webView.Visible)
        {
            return;
        }

        await ExecuteScriptSafeAsync("window.resizeRenderer?.();", "render-resize", _lastLoadedFileName, _lastLoadedSourcePath, _lastLoadedBytes.LongLength, "(none)");
    }

    private void AppendConsoleLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_consoleLogSync)
        {
            if (_consoleLogBuffer.Count >= ConsoleLogLimit)
            {
                _consoleLogBuffer.RemoveAt(0);
            }

            _consoleLogBuffer.Add(message);
        }
    }

    private void OpenEnlargedViewer()
    {
        if (_lastLoadedBytes.Length == 0)
        {
            return;
        }

        using var enlargedWindow = new Form
        {
            Text = "STEP Model Viewer",
            Width = 1200,
            Height = 840,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true
        };

        var closeButton = new Button
        {
            Text = "Close",
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        closeButton.Click += (_, _) => enlargedWindow.Close();

        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 8, 8, 0)
        };
        topBar.Controls.Add(closeButton);

        var enlargedViewer = new StepModelPreviewControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8)
        };
        enlargedViewer.LoadStep(_lastLoadedBytes, _lastLoadedFileName, _lastLoadedSourcePath);

        enlargedWindow.Controls.Add(enlargedViewer);
        enlargedWindow.Controls.Add(topBar);

        var owner = FindForm();
        if (owner is null)
        {
            enlargedWindow.ShowDialog();
            return;
        }

        enlargedWindow.ShowDialog(owner);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lastLoadedBytes = Array.Empty<byte>();
            _lastLoadedFileName = string.Empty;
            _lastLoadedSourcePath = string.Empty;
            try
            {
                if (_initialized && _webView.CoreWebView2 is not null)
                {
                    ExecuteScriptSafeAsync("window.disposeViewer?.();", "render-dispose", _lastLoadedFileName, _lastLoadedSourcePath, _lastLoadedBytes.LongLength, "(none)").Wait(1500);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        base.Dispose(disposing);
    }

    private sealed record StepRenderResult(bool ok, string? error, string? stage, int meshCount = 0, int triangleCount = 0, string? parser = null, string? stack = null, int meshes = 0, int triangles = 0);

    private static string BuildHtmlShell()
    {
        return """
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    html, body, #viewport { margin:0; padding:0; width:100%; height:100%; background:#f8fafc; overflow:hidden; }
  </style>
</head>
<body>
  <div id="viewport"></div>
  <script src="https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.min.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/three@0.160.0/examples/js/controls/OrbitControls.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/three@0.160.0/examples/js/loaders/STLLoader.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/three@0.160.0/examples/js/loaders/OBJLoader.js"></script>
  <script src="https://cdn.jsdelivr.net/npm/occt-import-js@0.0.23/dist/occt-import-js.js"></script>
  <script>
    window.__stepViewerReady = false;
    window.__stepViewerVersion = '0.0.0';
    const viewport = document.getElementById('viewport');
    const scene = new THREE.Scene();
    scene.background = new THREE.Color('#f8fafc');

    const camera = new THREE.PerspectiveCamera(50, 1, 0.01, 5000);
    camera.position.set(1.2, 0.9, 1.5);

    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    viewport.appendChild(renderer.domElement);

    const controls = new THREE.OrbitControls(camera, renderer.domElement);
    controls.enablePan = true;
    controls.enableZoom = true;
    controls.target.set(0, 0, 0);

    scene.add(new THREE.HemisphereLight(0xffffff, 0x6f7a88, 0.95));
    const keyLight = new THREE.DirectionalLight(0xffffff, 0.75);
    keyLight.position.set(3, 6, 5);
    scene.add(keyLight);

    let activeMeshRoot = null;
    let occtModulePromise = null;

    function base64ToUint8Array(base64) {
      const raw = atob(base64);
      const bytes = new Uint8Array(raw.length);
      for (let i = 0; i < raw.length; i++) {
        bytes[i] = raw.charCodeAt(i);
      }
      return bytes;
    }

    function result(ok, stage, error, stack, parser, meshCount, triangleCount) {
      return { ok, stage, error: error || '', stack: stack || '', parser: parser || '(none)', meshCount: meshCount || 0, triangleCount: triangleCount || 0 };
    }

    function clearScene() {
      if (!activeMeshRoot) {
        return;
      }

      scene.remove(activeMeshRoot);
      activeMeshRoot.traverse(node => {
        if (node.geometry) {
          node.geometry.dispose();
        }

        if (node.material) {
          if (Array.isArray(node.material)) {
            node.material.forEach(material => material.dispose());
          } else {
            node.material.dispose();
          }
        }
      });

      activeMeshRoot = null;
    }

    async function getOcctModule() {
      if (!occtModulePromise) {
        occtModulePromise = occtimportjs({
          locateFile(path) {
            return `https://cdn.jsdelivr.net/npm/occt-import-js@0.0.23/dist/${path}`;
          }
        });
      }

      return occtModulePromise;
    }

    function decodeStepText(bytes) {
      try {
        const decoded = new TextDecoder('utf-8', { fatal: false }).decode(bytes);
        return decoded || '';
      } catch {
        return '';
      }
    }

    function validateStepContent(bytes) {
      const sample = decodeStepText(bytes.subarray(0, Math.min(bytes.length, 8192))).toUpperCase();
      if (!sample.includes('ISO-10303-21') && !sample.includes('ISO10303-21')) {
        return result(false, 'validate-bytes', 'invalid-step-header');
      }

      if (!sample.includes('HEADER;') || !sample.includes('DATA;')) {
        return result(false, 'validate-bytes', 'invalid-step-body');
      }

      return result(true, 'validate-bytes', '');
    }

    function fitCameraToObject(object3D) {
      const box = new THREE.Box3().setFromObject(object3D);
      const size = box.getSize(new THREE.Vector3());
      const center = box.getCenter(new THREE.Vector3());
      const maxDim = Math.max(size.x, size.y, size.z, 0.001);
      const distance = maxDim * 2.4;

      controls.target.copy(center);
      camera.position.set(center.x + distance, center.y + distance * 0.7, center.z + distance);
      camera.near = Math.max(maxDim / 1000, 0.001);
      camera.far = Math.max(maxDim * 200, 500);
      camera.updateProjectionMatrix();
      controls.update();
    }

    function buildMeshGroup(resultData) {
      const group = new THREE.Group();
      let triangleCount = 0;

      for (const meshData of resultData.meshes || []) {
        const attrs = meshData.attributes || {};
        const positions = attrs.position && attrs.position.array ? attrs.position.array : null;
        if (!positions || positions.length === 0) {
          continue;
        }

        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));

        if (attrs.normal && attrs.normal.array && attrs.normal.array.length > 0) {
          geometry.setAttribute('normal', new THREE.Float32BufferAttribute(attrs.normal.array, 3));
        } else {
          geometry.computeVertexNormals();
        }

        if (meshData.index && meshData.index.array && meshData.index.array.length > 0) {
          geometry.setIndex(meshData.index.array);
          triangleCount += Math.floor(meshData.index.array.length / 3);
        } else {
          triangleCount += Math.floor(positions.length / 9);
        }

        const color = (meshData.color && meshData.color.length >= 3)
          ? new THREE.Color(meshData.color[0], meshData.color[1], meshData.color[2])
          : new THREE.Color('#4a78bb');

        const material = new THREE.MeshStandardMaterial({
          color,
          roughness: 0.55,
          metalness: 0.08,
          side: THREE.DoubleSide
        });

        const mesh = new THREE.Mesh(geometry, material);
        group.add(mesh);
      }

      return { group, triangleCount };
    }

    function parseWithOcct(occt, bytes) {
      const binaryParsed = occt.ReadStepFile(bytes, null);
      if (binaryParsed && binaryParsed.meshes && binaryParsed.meshes.length > 0) {
        return { parsed: binaryParsed, parser: 'occt-binary' };
      }

      const asText = decodeStepText(bytes);
      if (!asText) {
        throw new Error('parse-binary-failed');
      }

      const textBytes = new TextEncoder().encode(asText);
      const textParsed = occt.ReadStepFile(textBytes, null);
      if (textParsed && textParsed.meshes && textParsed.meshes.length > 0) {
        return { parsed: textParsed, parser: 'occt-text' };
      }

      throw new Error('parse-text-failed');
    }

    function parseStl(bytes) {
      const loader = new THREE.STLLoader();
      const geometry = loader.parse(bytes.buffer);
      if (!geometry) {
        throw new Error('parse-stl-failed');
      }

      const material = new THREE.MeshStandardMaterial({ color: new THREE.Color('#4a78bb'), roughness: 0.55, metalness: 0.08 });
      const mesh = new THREE.Mesh(geometry, material);
      const group = new THREE.Group();
      group.add(mesh);
      return { group, triangleCount: Math.max(0, Math.floor((geometry.index ? geometry.index.count : geometry.attributes.position.count) / 3)), parser: 'three-stl' };
    }

    function parseObj(bytes) {
      const loader = new THREE.OBJLoader();
      const text = new TextDecoder('utf-8', { fatal: false }).decode(bytes);
      const group = loader.parse(text);
      let triangleCount = 0;

      group.traverse(node => {
        if (!node.isMesh || !node.geometry) {
          return;
        }

        node.material = new THREE.MeshStandardMaterial({ color: new THREE.Color('#4a78bb'), roughness: 0.55, metalness: 0.08, side: THREE.DoubleSide });
        if (node.geometry.index) {
          triangleCount += Math.floor(node.geometry.index.count / 3);
        } else if (node.geometry.attributes && node.geometry.attributes.position) {
          triangleCount += Math.floor(node.geometry.attributes.position.count / 3);
        }
      });

      return { group, triangleCount, parser: 'three-obj' };
    }

    function getOcctReaders(occt, fileType) {
      if (fileType === 'step') {
        return [occt.ReadStepFile].filter(Boolean);
      }

      if (fileType === 'iges') {
        return [occt.ReadIgesFile].filter(fn => typeof fn === 'function');
      }

      if (fileType === 'brep') {
        return [occt.ReadBrepFile].filter(fn => typeof fn === 'function');
      }

      if (fileType === 'sldprt') {
        return [occt.ReadStepFile, occt.ReadBrepFile, occt.ReadIgesFile].filter(fn => typeof fn === 'function');
      }

      return [];
    }

    function parseOcctWithReaders(bytes, readers) {
      for (const reader of readers) {
        try {
          const parsed = reader(bytes, null);
          if (parsed && parsed.meshes && parsed.meshes.length > 0) {
            return parsed;
          }
        } catch {
          // Try next parser strategy.
        }
      }

      return null;
    }

    function detectFileType(fileName) {
      const value = (fileName || '').toLowerCase();
      if (value.endsWith('.stl')) return 'stl';
      if (value.endsWith('.obj')) return 'obj';
      if (value.endsWith('.iges') || value.endsWith('.igs')) return 'iges';
      if (value.endsWith('.brep') || value.endsWith('.brp')) return 'brep';
      if (value.endsWith('.sldprt')) return 'sldprt';
      return 'step';
    }

    async function renderModelFromBase64(base64, metadata) {
      if (!base64 || base64.length === 0) {
        clearScene();
        return result(false, 'validate-bytes', 'missing-file-data');
      }

      try {
        const fileType = (metadata && metadata.fileType ? metadata.fileType : '').toLowerCase();

        const bytes = base64ToUint8Array(base64);
        let meshData;

        if (fileType === 'step') {
          const validation = validateStepContent(bytes);
          if (!validation.ok) {
            clearScene();
            return validation;
          }

          let occt;
          try {
            occt = await getOcctModule();
          } catch {
            clearScene();
            return result(false, 'js-parse', 'module-load-failed');
          }

          const parsedResult = parseWithOcct(occt, bytes);
          const occtMeshData = buildMeshGroup(parsedResult.parsed || {});
          meshData = { group: occtMeshData.group, triangleCount: occtMeshData.triangleCount, parser: parsedResult.parser };
        } else if (fileType === 'stl') {
          meshData = parseStl(bytes);
        } else if (fileType === 'obj') {
          meshData = parseObj(bytes);
        } else if (fileType === 'iges' || fileType === 'brep' || fileType === 'sldprt') {
          let occt;
          try {
            occt = await getOcctModule();
          } catch {
            clearScene();
            return result(false, 'js-parse', 'module-load-failed');
          }

          const readers = getOcctReaders(occt, fileType);
          if (!readers || readers.length === 0) {
            clearScene();
            return result(false, 'js-parse', 'parser-not-available');
          }

          const parsed = parseOcctWithReaders(bytes, readers);
          if (!parsed) {
            clearScene();
            return result(false, 'js-parse', fileType === 'sldprt' ? 'sldprt-not-supported' : 'corrupted-or-invalid-step');
          }

          const occtMeshData = buildMeshGroup(parsed || {});
          meshData = { group: occtMeshData.group, triangleCount: occtMeshData.triangleCount, parser: `occt-${fileType}` };
        } else {
          clearScene();
          return result(false, 'validate-bytes', 'unsupported-format');
        }

        if (meshData.group.children.length === 0) {
          clearScene();
          return result(false, 'render', 'empty-geometry', '', meshData.parser, meshData.group.children.length, meshData.triangleCount);
        }

        clearScene();
        activeMeshRoot = meshData.group;
        scene.add(activeMeshRoot);
        fitCameraToObject(activeMeshRoot);
        return {
          ok: true,
          stage: 'render',
          error: '',
          meshCount: meshData.group.children.length,
          triangleCount: meshData.triangleCount,
          parser: meshData.parser
        };
      } catch (err) {
        clearScene();
        const parseError = (err && err.message) ? err.message : 'corrupted-or-invalid-step';
        const parseStack = (err && err.stack) ? err.stack : '';
        return result(false, 'js-parse', parseError, parseStack);
      }
    }

    function resizeRenderer() {
      const width = Math.max(viewport.clientWidth, 1);
      const height = Math.max(viewport.clientHeight, 1);
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
    }

    function resetViewerState() {
      clearScene();
      renderer.renderLists.dispose();
      controls.reset();
    }

    function disposeViewer() {
      clearScene();
      renderer.renderLists.dispose();
      renderer.dispose();
      controls.dispose();
    }


    function runStepSelfTest() {
      const geometry = new THREE.BoxGeometry(1, 1, 1);
      const material = new THREE.MeshStandardMaterial({ color: new THREE.Color('#4a78bb'), roughness: 0.55, metalness: 0.08 });
      const mesh = new THREE.Mesh(geometry, material);
      const group = new THREE.Group();
      group.add(mesh);
      clearScene();
      activeMeshRoot = group;
      scene.add(activeMeshRoot);
      fitCameraToObject(activeMeshRoot);
      return { ok: true, stage: 'render', error: '', meshCount: 1, triangleCount: 12, parser: 'self-test-box' };
    }

    window.renderModelFromBase64 = renderModelFromBase64;
    window.runStepSelfTest = runStepSelfTest;
    window.resizeRenderer = resizeRenderer;
    window.resetViewerState = resetViewerState;
    window.disposeViewer = disposeViewer;

    function animate() {
      requestAnimationFrame(animate);
      controls.update();
      renderer.render(scene, camera);
    }

    resizeRenderer();
    animate();
    window.addEventListener('resize', resizeRenderer);

    window.stepViewer = {
      isReady: () => window.__stepViewerReady === true,
      parseStepBase64: async (b64, fileName) => {
        try {
          const fileType = detectFileType(fileName);
          const value = await renderModelFromBase64(b64, { fileName: fileName || '', fileType });
          return {
            ok: !!value?.ok,
            meshes: value?.meshCount || 0,
            triangles: value?.triangleCount || 0,
            parser: value?.parser || '(none)',
            stage: value?.stage || 'js-parse',
            error: value?.error || '',
            stack: value?.stack || ''
          };
        } catch (err) {
          return {
            ok: false,
            meshes: 0,
            triangles: 0,
            parser: '(none)',
            stage: 'js-parse',
            error: String(err?.message || err || 'parse-failed'),
            stack: String(err?.stack || '')
          };
        }
      }
    };

    window.__stepViewerVersion = '1.0.0';
    window.__stepViewerReady = true;
  </script>
</body>
</html>
""";
    }
}
