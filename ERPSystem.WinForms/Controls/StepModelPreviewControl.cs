using System.Collections.Concurrent;
using System.Text.Json;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ERPSystem.WinForms.Controls;

public sealed class StepModelPreviewControl : UserControl
{
    private readonly WebView2 _webView;
    private readonly Label _statusLabel;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly IStepPreviewService? _stepPreviewService;
    private readonly StepParsingDiagnosticsLog? _diagnosticsLog;
    private readonly ConcurrentQueue<string> _lastWebMessages = new();
    private CancellationTokenSource? _loadCts;
    private int _renderVersion;
    private bool _initialized;
    private TaskCompletionSource<GltfViewerResult>? _viewerMessageTcs;
    private const string ViewerAssetRelativePath = "Assets/gltf-viewer.html";
    private const string ViewerAssetUri = "https://step-viewer.local/gltf-viewer.html";

    public StepModelPreviewControl(StepParsingDiagnosticsLog? diagnosticsLog = null, IStepPreviewService? stepPreviewService = null)
    {
        _diagnosticsLog = diagnosticsLog;
        _stepPreviewService = stepPreviewService;
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

        Controls.Add(_webView);
        Controls.Add(_statusLabel);
    }

    public void LoadStep(byte[]? stepBytes, string? fileName = null, string? sourcePath = null)
    {
        var bytes = stepBytes?.ToArray() ?? Array.Empty<byte>();
        var version = Interlocked.Increment(ref _renderVersion);
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _loadCts, cts);
        old?.Cancel();
        old?.Dispose();
        _ = LoadInternalAsync(bytes, fileName ?? string.Empty, sourcePath ?? string.Empty, version, cts.Token);
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

        var version = Interlocked.Increment(ref _renderVersion);
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _loadCts, cts);
        old?.Cancel();
        old?.Dispose();
        _ = LoadInternalAsync(data, attachment.FileName, attachment.StorageRelativePath, version, cts.Token);
    }

    public void ClearPreview()
    {
        var old = Interlocked.Exchange(ref _loadCts, null);
        old?.Cancel();
        old?.Dispose();
        _webView.Visible = false;
        _statusLabel.Visible = true;
        _statusLabel.Text = "3D preview cleared";
    }

    public Task<bool> RunDeveloperDiagnosticsProbeAsync() => Task.FromResult(true);

    public async Task<bool> RunStepSelfTestAsync()
    {
        try
        {
            var samplePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Samples", "known-good.step");
            if (!File.Exists(samplePath))
            {
                return false;
            }

            var bytes = await File.ReadAllBytesAsync(samplePath);
            LoadStep(bytes, "known-good.step", samplePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task LoadInternalAsync(byte[] stepBytes, string fileName, string sourcePath, int version, CancellationToken cancellationToken)
    {
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            var validation = ValidateStep(stepBytes);
            RecordDiagnostics(validation.valid, validation.errorCode, "step-validate", "STEP_PREVIEW", validation.message, stepBytes.LongLength, fileName, sourcePath, validation.details);
            if (!validation.valid)
            {
                ShowError(validation.message);
                return;
            }

            if (_stepPreviewService is null)
            {
                const string message = "STEP preview service unavailable.";
                ShowError(message);
                RecordDiagnostics(false, "STEP_SERVICE_MISSING", "step-convert", "STEP_PREVIEW", message, stepBytes.LongLength, fileName, sourcePath, "service-null");
                return;
            }

            _statusLabel.Text = "Converting STEP to GLB...";
            var conversion = await _stepPreviewService.GetOrCreateGlbDetailedAsync(stepBytes, fileName, cancellationToken);
            RecordDiagnostics(conversion.Success, conversion.ErrorCode, "step-convert", "STEP_PREVIEW", conversion.Message, stepBytes.LongLength, fileName, sourcePath,
                $"hash={conversion.StepHash};cacheHit={conversion.CacheHit};validation={conversion.ValidationDetails};exitCode={conversion.ExitCode};stdout={conversion.StdOut};stderr={conversion.StdErr}");
            if (!conversion.Success)
            {
                ShowError("STEP conversion failed. See diagnostics for details.");
                return;
            }

            if (version != _renderVersion || IsDisposed)
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken);
            if (version != _renderVersion || IsDisposed)
            {
                return;
            }

            _statusLabel.Text = "Rendering GLB...";
            var renderResult = await RenderGlbAsync(conversion.GlbBytes, cancellationToken);
            if (!renderResult.ok)
            {
                ShowError("GLB render failed. See diagnostics for details.");
                RecordDiagnostics(false, "GLB_RENDER_FAILED", "glb-render", "STEP_PREVIEW", renderResult.error ?? "GLB render failed.", conversion.GlbBytes.LongLength, fileName, sourcePath,
                    $"hash={conversion.StepHash};meshes={renderResult.meshes};triangles={renderResult.triangles};stack={renderResult.stack};webMessages={string.Join(" | ", _lastWebMessages)}");
                return;
            }

            _statusLabel.Visible = false;
            _webView.Visible = true;
            RecordDiagnostics(true, string.Empty, "glb-render", "STEP_PREVIEW", "GLB rendered.", conversion.GlbBytes.LongLength, fileName, sourcePath,
                $"hash={conversion.StepHash};meshes={renderResult.meshes};triangles={renderResult.triangles}");
        }
        catch (OperationCanceledException)
        {
            RecordDiagnostics(false, "STEP_LOAD_CANCELLED", "glb-render", "STEP_PREVIEW", "STEP preview cancelled.", stepBytes.LongLength, fileName, sourcePath, "cancelled");
        }
        catch (Exception ex)
        {
            ShowError("WebView2 runtime unavailable for GLB preview.");
            RecordDiagnostics(false, "GLB_RENDER_FAILED", "glb-render", "STEP_PREVIEW", ex.Message, stepBytes.LongLength, fileName, sourcePath,
                $"{ex};webMessages={string.Join(" | ", _lastWebMessages)}");
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _webView.CoreWebView2 is not null)
        {
            return;
        }

        await _webView.EnsureCoreWebView2Async();
        var viewerPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ViewerAssetRelativePath));
        var viewerFolder = Path.GetDirectoryName(viewerPath) ?? throw new InvalidOperationException("Viewer folder unavailable.");
        _webView.CoreWebView2!.WebMessageReceived -= CoreWebView2OnWebMessageReceived;
        _webView.CoreWebView2.WebMessageReceived += CoreWebView2OnWebMessageReceived;
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("step-viewer.local", viewerFolder, CoreWebView2HostResourceAccessKind.Allow);
        _webView.CoreWebView2.Navigate(ViewerAssetUri);
        _initialized = true;

        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalSeconds < 10)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ready = await _webView.CoreWebView2.ExecuteScriptAsync("document.readyState === 'complete'");
            if (IsScriptBooleanTrue(ready))
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("GLB viewer failed to initialize.");
    }

    private async Task<GltfViewerResult> RenderGlbAsync(byte[] glbBytes, CancellationToken cancellationToken)
    {
        _viewerMessageTcs = new TaskCompletionSource<GltfViewerResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        while (_lastWebMessages.TryDequeue(out _)) { }

        var payload = JsonSerializer.Serialize(Convert.ToBase64String(glbBytes));
        _ = await _webView.CoreWebView2!.ExecuteScriptAsync($"window.gltfViewer.setGlbBase64({payload});");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        using var registration = timeoutCts.Token.Register(() => _viewerMessageTcs.TrySetCanceled(timeoutCts.Token));
        return await _viewerMessageTcs.Task;
    }

    private void CoreWebView2OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var raw = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var message = JsonSerializer.Deserialize<ViewerMessage>(raw);
            if (message?.type is null)
            {
                return;
            }

            var compact = $"{message.type}:{JsonSerializer.Serialize(message.payload)}";
            _lastWebMessages.Enqueue(compact);
            while (_lastWebMessages.Count > 12 && _lastWebMessages.TryDequeue(out _)) { }

            if (message.type == "viewer-result" && message.payload is not null)
            {
                var result = message.payload.Deserialize<GltfViewerResult>() ?? new GltfViewerResult { ok = false, error = "Viewer returned invalid payload." };
                _viewerMessageTcs?.TrySetResult(result);
                return;
            }

            var isError = string.Equals(message.type, "viewer-error", StringComparison.OrdinalIgnoreCase);
            RecordDiagnostics(!isError, isError ? "GLB_RENDER_FAILED" : string.Empty, "glb-render", "STEP_PREVIEW", "Viewer message", 0, string.Empty, string.Empty, compact);
        }
        catch
        {
            // ignored
        }
    }

    private static (bool valid, string errorCode, string message, string details) ValidateStep(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return (false, "STEP_INVALID_BYTES", "STEP payload is empty.", "length=0");
        }

        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 64)).ToUpperInvariant();
        if (!header.Contains("ISO-10303-21"))
        {
            return (false, "STEP_INVALID_HEADER", "STEP header marker missing.", header);
        }

        return (true, string.Empty, "STEP validated.", "iso-10303-21");
    }

    private static bool IsScriptBooleanTrue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim().Trim('"');
        return string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.Visible = true;
        _webView.Visible = false;
    }

    private void RecordDiagnostics(bool isSuccess, string errorCode, string stage, string category, string message, long fileSizeBytes, string fileName, string sourcePath, string details)
    {
        _diagnosticsLog?.RecordAttempt(
            fileName: string.IsNullOrWhiteSpace(fileName) ? "(unknown)" : fileName,
            filePath: sourcePath,
            fileSizeBytes: fileSizeBytes,
            isSuccess: isSuccess,
            errorCode: errorCode,
            failureCategory: category,
            message: $"[{stage}] {message}",
            diagnosticDetails: details,
            stackTrace: isSuccess ? string.Empty : StepParsingDiagnosticsLog.BuildCallSiteTrace(),
            source: "step-preview");
    }

    private sealed class ViewerMessage
    {
        public string? type { get; set; }
        public JsonElement? payload { get; set; }
    }

    private sealed class GltfViewerResult
    {
        public bool ok { get; set; }
        public int meshes { get; set; }
        public int triangles { get; set; }
        public string? error { get; set; }
        public string? stack { get; set; }
    }
}
