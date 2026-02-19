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
    private bool _webViewLifecycleHooked;
    private bool _coreInitCompleted;
    private bool _viewerNavigationCompleted;
    private string _lastNavigationUri = "about:blank";
    private int _renderVersion;
    private TaskCompletionSource<bool>? _navigationReady;
    private TaskCompletionSource<bool>? _coreInitReady;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _loadCts;
    private readonly StepFileParser _stepFileParser = new();
    private readonly SolidModelFileTypeDetector _fileTypeDetector = new();
    private byte[] _lastLoadedBytes = Array.Empty<byte>();
    private string _lastLoadedFileName = string.Empty;
    private string _lastLoadedSourcePath = string.Empty;
    private const string ViewerAssetRelativePath = "Assets/step-viewer.html";
    private const string ViewerAssetUri = "https://step-viewer.local/step-viewer.html";
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
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        _ = LoadStepInternalAsync(bytesSnapshot, nameSnapshot, pathSnapshot, version, cts.Token);
    }

    public void ClearPreview()
    {
        Interlocked.Increment(ref _renderVersion);
        var previous = Interlocked.Exchange(ref _loadCts, null);
        previous?.Cancel();
        previous?.Dispose();
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
        using var selfTestCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await EnsureInitializedAsync("embedded-self-test.step", "embedded://self-test", 0, "self-test", selfTestCts.Token);
        if (!await WaitForViewerReadyAsync("embedded-self-test.step", "embedded://self-test", 0, "self-test", selfTestCts.Token, ViewerReadyTimeoutMs))
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

    private async Task LoadStepInternalAsync(byte[] stepBytes, string sourceFileName, string sourcePath, int version, CancellationToken cancellationToken)
    {
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearConsoleBuffer();
            await ResetViewerForNextParseAsync(cancellationToken);

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

            await EnsureInitializedAsync(sourceFileName, sourcePath, stepBytes.LongLength, parser, cancellationToken);
            if (version != _renderVersion)
            {
                return;
            }

            stage = "viewer-ready";
            if (!await WaitForViewerReadyAsync(sourceFileName, sourcePath, stepBytes.LongLength, parser, cancellationToken, ViewerReadyTimeoutMs))
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
            const int maxAttempts = 3;
            var backoffMs = new[] { 250, 750, 1750 };
            string? resultRaw = null;
            (bool Ok, string Error, string Stage, int MeshCount, int TriangleCount, string Parser) result = (false, "parse-failed", stage, 0, 0, parser);

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                resultRaw = await ExecuteScriptSafeAsync($"window.stepViewer.parseStepBase64({payload}, {fileNameArg});", stage, sourceFileName, sourcePath, stepBytes.LongLength, parser);
                if (string.IsNullOrWhiteSpace(resultRaw))
                {
                    if (attempt == maxAttempts)
                    {
                        ShowError("Unable to render STEP geometry (script-empty-result)");
                        var logs = DrainConsoleBuffer();
                        var readyState = await GetDocumentReadyStateAsync();
                        RecordDiagnostics(false, "STEP_JS_EMPTY_RESULT", "js-parse", "STEP_PREVIEW", "Unable to render STEP geometry (script-empty-result)", stepBytes.LongLength, sourceFileName, sourcePath,
                            BuildDiagnosticDetails(parser, "js-parse", meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, $"attempt={attempt}/{maxAttempts}; render-script-returned-empty; scriptLength={payload.Length}; readyState={readyState}; console={logs}"));
                        return;
                    }

                    RecordDiagnostics(false, "STEP_JS_RETRY", "js-parse", "STEP_PREVIEW", "STEP parse returned empty result; retrying.", stepBytes.LongLength, sourceFileName, sourcePath,
                        BuildDiagnosticDetails(parser, "js-parse", meshCount, triangleCount, jsError, stepBytes.LongLength, sourceFileName, sourcePath, $"attempt={attempt}/{maxAttempts}; backoffMs={backoffMs[attempt - 1]}"));
                    await Task.Delay(backoffMs[attempt - 1], cancellationToken);
                    continue;
                }

                result = ParseScriptResult(resultRaw);
                if (result.Ok || !result.Error.Contains("empty-geometry", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (attempt < maxAttempts)
                {
                    RecordDiagnostics(false, "STEP_JS_RETRY", "js-parse", "STEP_PREVIEW", "STEP parse returned retryable result; retrying.", stepBytes.LongLength, sourceFileName, sourcePath,
                        BuildDiagnosticDetails(parser, "js-parse", meshCount, triangleCount, result.Error, stepBytes.LongLength, sourceFileName, sourcePath, $"attempt={attempt}/{maxAttempts}; backoffMs={backoffMs[attempt - 1]}"));
                    await Task.Delay(backoffMs[attempt - 1], cancellationToken);
                }
            }

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
        catch (OperationCanceledException)
        {
            RecordDiagnostics(false, "STEP_LOAD_CANCELLED", "load-cancelled", "STEP_PREVIEW", "STEP load was cancelled due to a newer request.", stepBytes.LongLength, sourceFileName, sourcePath,
                BuildDiagnosticDetails("(none)", "load-cancelled", 0, 0, "cancelled", stepBytes.LongLength, sourceFileName, sourcePath, $"version={version}"));
        }
        finally
        {
            if (_loadGate.CurrentCount == 0)
            {
                _loadGate.Release();
            }
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

    private async Task ResetViewerForNextParseAsync(CancellationToken cancellationToken)
    {
        _statusLabel.Visible = true;
        _webView.Visible = false;

        if (!_initialized || _webView.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private async Task<bool> WaitForViewerReadyAsync(string sourceFileName, string sourcePath, long fileSizeBytes, string parser, CancellationToken cancellationToken, int timeoutMs = ViewerReadyTimeoutMs)
    {
        if (!_coreInitCompleted || !_viewerNavigationCompleted)
        {
            RecordDiagnostics(false, "STEP_VIEWER_NAV_NOT_READY", "viewer-ready", "STEP_PREVIEW", "Viewer readiness polling blocked until initialization and navigation are complete.", fileSizeBytes, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, "viewer-ready", 0, 0, string.Empty, fileSizeBytes, sourceFileName, sourcePath, $"initCompleted={_coreInitCompleted}; navigationCompleted={_viewerNavigationCompleted}; lastUri={_lastNavigationUri}"));
            return false;
        }

        var sw = Stopwatch.StartNew();
        string lastReadyState = "(unknown)";
        string viewerStatus = "null";
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readyRaw = await ExecuteScriptSafeAsync("window.stepViewer && window.stepViewer.isReady && window.stepViewer.isReady()", "viewer-ready", sourceFileName, sourcePath, fileSizeBytes, parser);
            if (IsScriptBooleanTrue(readyRaw))
            {
                return true;
            }

            lastReadyState = await GetDocumentReadyStateAsync();
            viewerStatus = await GetViewerStatusJsonAsync(sourceFileName, sourcePath, fileSizeBytes, parser);
            await Task.Delay(100, cancellationToken);
        }

        var logs = DrainConsoleBuffer();
        RecordDiagnostics(false, "STEP_VIEWER_NOT_READY", "viewer-ready", "STEP_PREVIEW", "STEP viewer did not report ready state before timeout.", fileSizeBytes, sourceFileName, sourcePath,
            BuildDiagnosticDetails(parser, "viewer-ready", 0, 0, string.Empty, fileSizeBytes, sourceFileName, sourcePath, $"timeoutMs={timeoutMs}; initCompleted={_coreInitCompleted}; navigationCompleted={_viewerNavigationCompleted}; lastUri={_lastNavigationUri}; readyState={lastReadyState}; viewerStatus={viewerStatus}; console={logs}"));
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

    private async Task<string> GetViewerStatusJsonAsync(string sourceFileName, string sourcePath, long fileSizeBytes, string parser)
    {
        var raw = await ExecuteScriptSafeAsync("JSON.stringify(window.__stepViewerStatus || null)", "viewer-ready", sourceFileName, sourcePath, fileSizeBytes, parser);
        return string.IsNullOrWhiteSpace(raw) ? "null" : raw.Trim().Trim('"').Replace("\\\"", "\"");
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

    private async Task EnsureInitializedAsync(string sourceFileName, string sourcePath, long fileSizeBytes, string parser, CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        _coreInitReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _viewerNavigationCompleted = false;
        _coreInitCompleted = false;

        _webView.CoreWebView2InitializationCompleted += (_, args) =>
        {
            _coreInitCompleted = args.IsSuccess;
            var details = $"isSuccess={args.IsSuccess}; exception={args.InitializationException?.Message ?? string.Empty}";
            RecordDiagnostics(args.IsSuccess, args.IsSuccess ? string.Empty : "STEP_WEBVIEW_INIT_FAILED", "core-init", "STEP_PREVIEW", "WebView2 initialization completed.", fileSizeBytes, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, "core-init", 0, 0, args.InitializationException?.ToString() ?? string.Empty, fileSizeBytes, sourceFileName, sourcePath, details));
            if (args.IsSuccess)
            {
                _coreInitReady?.TrySetResult(true);
            }
            else
            {
                _coreInitReady?.TrySetException(args.InitializationException ?? new InvalidOperationException("WebView2 initialization failed."));
            }
        };

        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

        if (!_webViewLifecycleHooked)
        {
            _webViewLifecycleHooked = true;
            _webView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                _lastNavigationUri = args.Uri ?? "(unknown)";
                AppendConsoleLog($"[NavigationStarting] uri={_lastNavigationUri}");
            };

            _webView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                var uri = _webView.CoreWebView2.Source ?? _lastNavigationUri;
                _lastNavigationUri = uri;
                _viewerNavigationCompleted = args.IsSuccess && string.Equals(uri, ViewerAssetUri, StringComparison.OrdinalIgnoreCase);
                AppendConsoleLog($"[NavigationCompleted] isSuccess={args.IsSuccess}; errorStatus={args.WebErrorStatus}; uri={uri}");
                RecordDiagnostics(args.IsSuccess, args.IsSuccess ? string.Empty : "STEP_WEBVIEW_NAV_FAILED", "navigation-completed", "STEP_PREVIEW", "WebView2 navigation completed.", _lastLoadedBytes.LongLength, _lastLoadedFileName, _lastLoadedSourcePath,
                    BuildDiagnosticDetails("(none)", "navigation-completed", 0, 0, args.WebErrorStatus.ToString(), _lastLoadedBytes.LongLength, _lastLoadedFileName, _lastLoadedSourcePath, $"isSuccess={args.IsSuccess}; uri={uri}"));
                if (args.IsSuccess)
                {
                    _navigationReady?.TrySetResult(true);
                }
                else
                {
                    _navigationReady?.TrySetException(new InvalidOperationException($"Failed to load STEP viewer shell at {uri}."));
                }
            };

            _webView.CoreWebView2.ProcessFailed += (_, args) =>
            {
                var reason = args.Reason.ToString();
                var exitCode = GetProcessExitCodeSafe(args);
                AppendConsoleLog($"[ProcessFailed] kind={args.ProcessFailedKind}; reason={reason}; exitCode={exitCode}");
                RecordDiagnostics(false, "STEP_WEBVIEW_PROCESS_FAILED", "viewer-ready", "STEP_PREVIEW", "WebView2 process failure detected.", _lastLoadedBytes.LongLength, _lastLoadedFileName, _lastLoadedSourcePath,
                    BuildDiagnosticDetails("(none)", "viewer-ready", 0, 0, reason, _lastLoadedBytes.LongLength, _lastLoadedFileName, _lastLoadedSourcePath, $"processKind={args.ProcessFailedKind}; exitCode={exitCode}"));
            };

            _webView.CoreWebView2.ConsoleMessageReceived += (_, args) =>
            {
                AppendConsoleLog($"[Console:{args.Level}] {args.Message}");
            };
        }

        var viewerPath = ResolveViewerAssetPath();
        if (!File.Exists(viewerPath))
        {
            RecordDiagnostics(false, "STEP_VIEWER_ASSET_MISSING", "asset-resolve", "STEP_PREVIEW", "STEP viewer asset missing on disk.", fileSizeBytes, sourceFileName, sourcePath,
                BuildDiagnosticDetails(parser, "asset-resolve", 0, 0, string.Empty, fileSizeBytes, sourceFileName, sourcePath, $"resolvedPath={viewerPath}; uri={ViewerAssetUri}"));
            throw new FileNotFoundException("STEP viewer asset missing.", viewerPath);
        }

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("step-viewer.local", Path.GetDirectoryName(viewerPath)!, CoreWebView2HostResourceAccessKind.Allow);
        AppendConsoleLog($"[ViewerAsset] path={viewerPath}; uri={ViewerAssetUri}");
        RecordDiagnostics(true, string.Empty, "asset-resolve", "STEP_PREVIEW", "Resolved STEP viewer asset path.", fileSizeBytes, sourceFileName, sourcePath,
            BuildDiagnosticDetails(parser, "asset-resolve", 0, 0, string.Empty, fileSizeBytes, sourceFileName, sourcePath, $"resolvedPath={viewerPath}; uri={ViewerAssetUri}"));

        _webView.CoreWebView2.Navigate(ViewerAssetUri);
        _initialized = true;
        if (_coreInitReady is not null)
        {
            await _coreInitReady.Task.WaitAsync(cancellationToken);
        }
        if (_navigationReady is not null)
        {
            await _navigationReady.Task.WaitAsync(cancellationToken);
        }
    }

    private static string ResolveViewerAssetPath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, ViewerAssetRelativePath));
    }

    private static string GetProcessExitCodeSafe(CoreWebView2ProcessFailedEventArgs args)
    {
        try
        {
            var value = args.GetType().GetProperty("ExitCode")?.GetValue(args);
            return value?.ToString() ?? "n/a";
        }
        catch
        {
            return "n/a";
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
            var previous = Interlocked.Exchange(ref _loadCts, null);
            previous?.Cancel();
            previous?.Dispose();
            _loadGate.Dispose();
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
}
