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
    private CancellationTokenSource? _loadCts;
    private int _renderVersion;
    private bool _initialized;
    private TaskCompletionSource<string>? _viewerMessageTcs;
    private const string ViewerAssetRelativePath = "Assets/step-viewer.html";
    private const string ViewerAssetUri = "https://step-viewer.local/step-viewer.html";
    private const int MaxRenderRetries = 2;

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
            RecordDiagnostics(true, "", "step-validate", "STEP_PREVIEW", "Validation started.", stepBytes.LongLength, fileName, sourcePath, "begin");

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

            if (version != _renderVersion)
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken);
            _statusLabel.Text = "Rendering GLB...";

            var renderResult = await RenderGlbAsync(conversion.GlbBytes, cancellationToken);
            if (!renderResult.ok)
            {
                ShowError("GLB render failed. See diagnostics for details.");
                RecordDiagnostics(false, "STEP_RENDER_FAILED", "glb-render", "STEP_PREVIEW", renderResult.error ?? "GLB render failed.", conversion.GlbBytes.LongLength, fileName, sourcePath,
                    $"hash={conversion.StepHash};meshes={renderResult.meshes};triangles={renderResult.triangles};stack={renderResult.stack}");
                return;
            }

            _statusLabel.Visible = false;
            _webView.Visible = true;
            RecordDiagnostics(true, "", "glb-render", "STEP_PREVIEW", "GLB rendered.", conversion.GlbBytes.LongLength, fileName, sourcePath,
                $"hash={conversion.StepHash};meshes={renderResult.meshes};triangles={renderResult.triangles}");
        }
        catch (OperationCanceledException)
        {
            RecordDiagnostics(false, "STEP_LOAD_CANCELLED", "glb-render", "STEP_PREVIEW", "STEP preview cancelled.", stepBytes.LongLength, fileName, sourcePath, "cancelled");
        }
        catch (Exception ex)
        {
            ShowError("WebView2 runtime unavailable for GLB preview.");
            RecordDiagnostics(false, "STEP_RENDER_FAILED", "glb-render", "STEP_PREVIEW", ex.Message, stepBytes.LongLength, fileName, sourcePath, ex.ToString());
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
        _webView.CoreWebView2!.WebMessageReceived -= CoreWebView2OnWebMessageReceived;
        _webView.CoreWebView2.WebMessageReceived += CoreWebView2OnWebMessageReceived;
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping("step-viewer.local", Path.GetDirectoryName(viewerPath)!, CoreWebView2HostResourceAccessKind.Allow);
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

    private async Task<(bool ok, int meshes, int triangles, string? error, string? stack)> RenderGlbAsync(byte[] glbBytes, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= MaxRenderRetries; attempt++)
        {
            try
            {
                _viewerMessageTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var b64 = Convert.ToBase64String(glbBytes);
                var payload = JsonSerializer.Serialize(b64);
                _ = await _webView.CoreWebView2!.ExecuteScriptAsync($"window.gltfViewer.loadGlbBase64({payload});");

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
                using var registration = timeoutCts.Token.Register(() => _viewerMessageTcs.TrySetCanceled(timeoutCts.Token));
                var raw = await _viewerMessageTcs.Task;
                var result = JsonSerializer.Deserialize<GltfViewerResult>(raw) ?? new GltfViewerResult();
                if (result.ok)
                {
                    return (true, result.meshes, result.triangles, null, null);
                }

                lastException = new InvalidOperationException(result.error ?? "Viewer returned failure.");
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        return (false, 0, 0, lastException?.Message, lastException?.ToString());
    }

    private void CoreWebView2OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var text = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (text.StartsWith("{", StringComparison.Ordinal))
            {
                _viewerMessageTcs?.TrySetResult(text);
                return;
            }

            RecordDiagnostics(true, "", "glb-render", "STEP_PREVIEW", "Viewer message", 0, string.Empty, string.Empty, text);
        }
        catch
        {
            // ignore
        }
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

    private sealed class GltfViewerResult
    {
        public bool ok { get; set; }
        public int meshes { get; set; }
        public int triangles { get; set; }
        public string? error { get; set; }
        public string? stack { get; set; }
    }
}
