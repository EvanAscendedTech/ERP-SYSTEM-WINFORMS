using ERPSystem.WinForms.Models;
using ERPSystem.WinForms.Services;
using ERPSystem.WinForms.WpfControls;
using System.Windows.Forms.Integration;

namespace ERPSystem.WinForms.Controls;

public sealed class ModelPreviewControl : UserControl
{
    private readonly ElementHost _viewportHost;
    private readonly HelixViewportHostControl _viewportControl;
    private readonly Label _statusLabel;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly StepParsingDiagnosticsLog? _diagnosticsLog;
    private CancellationTokenSource? _loadCts;

    public ModelPreviewControl(StepParsingDiagnosticsLog? diagnosticsLog = null, IModelPreviewService? modelPreviewService = null)
    {
        _diagnosticsLog = diagnosticsLog;
        BackColor = Color.FromArgb(248, 250, 252);

        _viewportControl = new HelixViewportHostControl();
        _viewportHost = new ElementHost
        {
            Dock = DockStyle.Fill,
            Visible = false,
            Child = _viewportControl
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.DimGray,
            Text = "Ready for 3D preview"
        };

        Controls.Add(_viewportHost);
        Controls.Add(_statusLabel);
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

            var effectiveName = string.IsNullOrWhiteSpace(fileName)
                ? Path.GetFileName(sourcePath ?? string.Empty)
                : fileName;

            var extension = Path.GetExtension(effectiveName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension))
            {
                ShowError("Unsupported file type. Only STL or OBJ can be previewed.");
                RecordDiagnostics(false, "MODEL_UNSUPPORTED_EXTENSION", "Missing file extension.", bytes.LongLength, effectiveName ?? string.Empty, sourcePath ?? string.Empty, "");
                return;
            }

            if (extension is ".step" or ".stp")
            {
                ShowError("STEP preview is not supported yet. Please upload STL or OBJ.");
                RecordDiagnostics(false, "STEP_NOT_SUPPORTED", "STEP preview is not supported yet.", bytes.LongLength, effectiveName ?? string.Empty, sourcePath ?? string.Empty, string.Empty);
                return;
            }

            if (extension is not ".stl" and not ".obj")
            {
                ShowError("Unsupported file type. Only STL or OBJ can be previewed.");
                RecordDiagnostics(false, "MODEL_UNSUPPORTED_EXTENSION", $"Extension '{extension}' is not supported.", bytes.LongLength, effectiveName ?? string.Empty, sourcePath ?? string.Empty, string.Empty);
                return;
            }

            _statusLabel.Text = "Loading 3D model...";
            cts.Token.ThrowIfCancellationRequested();
            _viewportControl.LoadModelFromBytes(bytes, effectiveName ?? string.Empty);
            _viewportControl.ZoomToFit();

            _statusLabel.Visible = false;
            _viewportHost.Visible = true;
            RecordDiagnostics(true, string.Empty, "Model loaded successfully.", bytes.LongLength, effectiveName ?? string.Empty, sourcePath ?? string.Empty, $"extension={extension}");
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
        _viewportHost.Visible = false;
        _statusLabel.Visible = true;
        _statusLabel.Text = "3D preview cleared";
    }

    private void ShowError(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.Visible = true;
        _viewportHost.Visible = false;
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
