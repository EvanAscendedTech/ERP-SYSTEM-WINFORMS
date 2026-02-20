using System.Text.Json;
using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ERPSystem.WinForms.Controls;

public sealed class ModelPreviewControl : UserControl
{
    private readonly WebView2 _webView;
    private readonly Label _statusLabel;
    private readonly Button _retryButton;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly IModelPreviewService? _modelPreviewService;
    private readonly StepParsingDiagnosticsLog? _diagnosticsLog;
    private CancellationTokenSource? _loadCts;
    private bool _initialized;
    private const string ViewerAssetRelativePath = "Assets/Viewer/model-viewer.html";
    private const string ViewerAssetUri = "https://model-viewer.local/model-viewer.html";

    public ModelPreviewControl(StepParsingDiagnosticsLog? diagnosticsLog = null, IModelPreviewService? modelPreviewService = null)
    {
        _diagnosticsLog = diagnosticsLog;
        _modelPreviewService = modelPreviewService;
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
            Text = "Ready for 3D preview"
        };

        _retryButton = new Button
        {
            Text = "Retry conversion",
            AutoSize = true,
            Visible = false,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _retryButton.Click += (_, _) =>
        {
            _modelPreviewService?.ResetConverterAvailability();
            _retryButton.Visible = false;
            _statusLabel.Text = "Converter reset. Click Preview 3D.";
        };

        Controls.Add(_webView);
        Controls.Add(_statusLabel);
        Controls.Add(_retryButton);
        Resize += (_, _) => PositionRetryButton();
        PositionRetryButton();
    }

    public async Task LoadAttachmentAsync(QuoteBlobAttachment? attachment, Func<int, Task<byte[]>>? blobResolver = null)
    {
        if (attachment is null)
        {
            ClearPreview();
            return;
        }

        var data = attachment.BlobData;
        if (data.Length == 0 && blobResolver is not null && attachment.Id > 0)
        {
            data = await blobResolver(attachment.Id);
        }

        await LoadModelAsync(data, attachment.FileName, attachment.StorageRelativePath);
    }

    public async Task LoadModelAsync(byte[]? modelBytes, string? fileName = null, string? sourcePath = null)
    {
        var bytes = modelBytes?.ToArray() ?? Array.Empty<byte>();
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _loadCts, cts);
        old?.Cancel();
        old?.Dispose();

        await _loadGate.WaitAsync(cts.Token);
        try
        {
            if (bytes.Length == 0)
            {
                ShowError("No 3D model data available.");
                return;
            }

            if (_modelPreviewService is null)
            {
                ShowError("3D preview service unavailable.");
                return;
            }

            _statusLabel.Text = "Converting model to GLB...";
            var conversion = await _modelPreviewService.GetOrCreateGlbAsync(bytes, fileName ?? string.Empty, cts.Token);
            RecordDiagnostics(conversion.Success, conversion.ErrorCode, conversion.Message, bytes.LongLength, fileName ?? string.Empty, sourcePath ?? string.Empty,
                $"hash={conversion.ModelHash};cacheHit={conversion.CacheHit};exitCode={conversion.ExitCode};stdout={conversion.StdOut};stderr={conversion.StdErr}");

            if (!conversion.Success)
            {
                ShowError("3D conversion failed. See diagnostics.");
                _retryButton.Visible = _modelPreviewService.IsConverterUnavailable;
                return;
            }

            await EnsureInitializedAsync();
            var payload = JsonSerializer.Serialize(Convert.ToBase64String(conversion.GlbBytes));
            await _webView.CoreWebView2!.ExecuteScriptAsync($"window.gltfViewer.setGlbBase64({payload});");

            _statusLabel.Visible = false;
            _webView.Visible = true;
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            ShowError("3D viewer failed to load.");
            RecordDiagnostics(false, "MODEL_RENDER_FAILED", ex.Message, bytes.LongLength, fileName ?? string.Empty, sourcePath ?? string.Empty, ex.ToString());
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void ClearPreview()
    {
        var old = Interlocked.Exchange(ref _loadCts, null);
        old?.Cancel();
        old?.Dispose();
        _webView.Visible = false;
        _statusLabel.Visible = true;
        _statusLabel.Text = "3D preview cleared";
        _retryButton.Visible = false;
    }

    private void PositionRetryButton()
    {
        _retryButton.Location = new Point(Math.Max(8, ClientSize.Width - _retryButton.Width - 8), Math.Max(8, ClientSize.Height - _retryButton.Height - 8));
        _retryButton.BringToFront();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized && _webView.CoreWebView2 is not null)
        {
            return;
        }

        await _webView.EnsureCoreWebView2Async();
        var viewerPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ViewerAssetRelativePath));
        var viewerFolder = Path.GetDirectoryName(viewerPath) ?? throw new InvalidOperationException("Viewer folder unavailable.");
        _webView.CoreWebView2!.SetVirtualHostNameToFolderMapping("model-viewer.local", viewerFolder, CoreWebView2HostResourceAccessKind.Allow);
        _webView.CoreWebView2.Navigate(ViewerAssetUri);
        _initialized = true;
    }

    private void ShowError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.Visible = true;
        _webView.Visible = false;
    }

    private void RecordDiagnostics(bool isSuccess, string errorCode, string message, long fileSizeBytes, string fileName, string sourcePath, string details)
    {
        _diagnosticsLog?.RecordAttempt(
            fileName: string.IsNullOrWhiteSpace(fileName) ? "(unknown)" : fileName,
            filePath: sourcePath,
            fileSizeBytes: fileSizeBytes,
            isSuccess: isSuccess,
            errorCode: errorCode,
            failureCategory: "MODEL_PREVIEW",
            message: message,
            diagnosticDetails: details,
            stackTrace: isSuccess ? string.Empty : StepParsingDiagnosticsLog.BuildCallSiteTrace(),
            source: "model-preview");
    }
}
