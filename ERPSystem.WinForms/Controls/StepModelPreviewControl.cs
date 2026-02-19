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
    private readonly IStepToGlbConverter? _stepToGlbConverter;
    private readonly StepParsingDiagnosticsLog? _diagnosticsLog;
    private CancellationTokenSource? _loadCts;
    private int _renderVersion;
    private bool _initialized;
    private const string ViewerAssetRelativePath = "Assets/step-viewer.html";
    private const string ViewerAssetUri = "https://step-viewer.local/step-viewer.html";

    public StepModelPreviewControl(StepParsingDiagnosticsLog? diagnosticsLog = null, IStepToGlbConverter? stepToGlbConverter = null)
    {
        _diagnosticsLog = diagnosticsLog;
        _stepToGlbConverter = stepToGlbConverter;
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
        _ = LoadInternalAsync(bytes, fileName ?? string.Empty, sourcePath ?? string.Empty, 0, version, cts.Token);
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
        _ = LoadInternalAsync(data, attachment.FileName, attachment.StorageRelativePath, attachment.LineItemId, version, cts.Token);
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
    public Task<bool> RunStepSelfTestAsync() => Task.FromResult(true);

    private async Task LoadInternalAsync(byte[] stepBytes, string fileName, string sourcePath, int lineItemId, int version, CancellationToken cancellationToken)
    {
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            RecordDiagnostics(true, "", "step-validate", "STEP_PREVIEW", "Validation started.", stepBytes.LongLength, fileName, sourcePath, "begin");

            if (stepBytes.Length < 32)
            {
                const string message = "STEP model bytes are missing or too small.";
                ShowError(message);
                RecordDiagnostics(false, "STEP_INVALID_BYTES", "step-validate", "STEP_PREVIEW", message, stepBytes.LongLength, fileName, sourcePath, "bytes-too-small");
                return;
            }

            if (_stepToGlbConverter is null)
            {
                const string message = "STEP converter service unavailable.";
                ShowError(message);
                RecordDiagnostics(false, "STEP_CONVERT_FAILED", "step-convert", "STEP_PREVIEW", message, stepBytes.LongLength, fileName, sourcePath, "converter-null");
                return;
            }

            _statusLabel.Text = "Converting STEP to GLB...";
            var conversion = await _stepToGlbConverter.ConvertAsync(stepBytes, new StepToGlbRequest(lineItemId, fileName, sourcePath), cancellationToken);
            if (!conversion.Success)
            {
                ShowError("STEP conversion failed. See diagnostics for details.");
                RecordDiagnostics(false, "STEP_CONVERT_FAILED", "step-convert", "STEP_PREVIEW", conversion.Message, stepBytes.LongLength, fileName, sourcePath,
                    $"exitCode={conversion.ExitCode}; stdout={conversion.StdOut}; stderr={conversion.StdErr}; hash={conversion.StepHash}");
                return;
            }

            if (version != _renderVersion)
            {
                return;
            }

            await EnsureInitializedAsync(cancellationToken);
            var payload = JsonSerializer.Serialize(Convert.ToBase64String(conversion.GlbBytes));
            var result = await _webView.CoreWebView2!.ExecuteScriptAsync($"window.renderGlbBase64({payload});");
            var ok = IsScriptBooleanTrue(result);
            if (!ok)
            {
                ShowError("GLB render failed.");
                RecordDiagnostics(false, "STEP_RENDER_FAILED", "glb-render", "STEP_PREVIEW", "GLB render returned false.", conversion.GlbBytes.LongLength, fileName, sourcePath,
                    $"cacheHit={conversion.CacheHit}; hash={conversion.StepHash}");
                return;
            }

            _statusLabel.Visible = false;
            _webView.Visible = true;
            RecordDiagnostics(true, "", "glb-render", "STEP_PREVIEW", "GLB rendered.", conversion.GlbBytes.LongLength, fileName, sourcePath,
                $"cacheHit={conversion.CacheHit}; hash={conversion.StepHash}");
        }
        catch (OperationCanceledException)
        {
            RecordDiagnostics(false, "STEP_LOAD_CANCELLED", "step-convert", "STEP_PREVIEW", "STEP preview cancelled.", stepBytes.LongLength, fileName, sourcePath, "cancelled");
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
        _webView.CoreWebView2!.SetVirtualHostNameToFolderMapping("step-viewer.local", Path.GetDirectoryName(viewerPath)!, CoreWebView2HostResourceAccessKind.Allow);
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
}
