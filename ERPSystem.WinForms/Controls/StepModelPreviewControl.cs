using Microsoft.Web.WebView2.WinForms;
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

    private async Task LoadStepInternalAsync(byte[] stepBytes, string sourceFileName, string sourcePath, int version)
    {
        await ResetViewerForNextParseAsync();

        if (version != _renderVersion)
        {
            return;
        }

        if (stepBytes.Length == 0)
        {
            const string message = "STEP model unavailable (missing file data)";
            ShowError(message);
            RecordDiagnostics(false, "missing-file-data", "file", message, "step-preview", stepBytes.LongLength, sourceFileName, sourcePath, "preview received empty payload");
            return;
        }

        var candidateName = !string.IsNullOrWhiteSpace(sourceFileName)
            ? sourceFileName
            : sourcePath;
        var detection = _fileTypeDetector.Detect(stepBytes, candidateName);
        if (!detection.IsKnownType)
        {
            const string message = "Unsupported 3D format. Supported: STEP/STP, STL, OBJ, IGES, BREP, SLDPRT";
            ShowError(message);
            RecordDiagnostics(false, "unsupported-format", "format", message, "step-preview", stepBytes.LongLength, sourceFileName, sourcePath, $"detection-source={detection.DetectionSource}");
            return;
        }

        if (!detection.IsSupportedForRendering)
        {
            var message = $"Detected {detection.FileType} format ({detection.NormalizedExtension}), but no parser kernel is configured for rendering.";
            ShowError(message);
            RecordDiagnostics(false, "parser-not-available", "parser", message, "step-preview", stepBytes.LongLength, sourceFileName, sourcePath, $"fileType={detection.FileType}; extension={detection.NormalizedExtension}");
            return;
        }

        _statusLabel.Text = $"Parsing {detection.FileType} geometry...";
        _statusLabel.Visible = true;
        Trace.WriteLine($"[StepPreview] Begin parse file='{candidateName}', bytes={stepBytes.Length}, type={detection.FileType}, source={detection.DetectionSource}");

        try
        {
            if (detection.FileType == SolidModelFileType.Step)
            {
                var parseReport = _stepFileParser.Parse(stepBytes);
                if (!parseReport.IsSuccess)
                {
                    Trace.WriteLine($"[StepPreview] STEP pre-parse failed error={parseReport.ErrorCode}, message='{parseReport.Message}'");
                    var message = BuildStatusMessage((false, parseReport.ErrorCode, "pre-parse", 0, 0, "step-precheck"));
                    ShowError(message);
                    RecordDiagnostics(false, parseReport.ErrorCode, parseReport.FailureCategory, message, "step-precheck", stepBytes.LongLength, sourceFileName, sourcePath, BuildPreParseDetails(parseReport));
                    return;
                }

                Trace.WriteLine($"[StepPreview] STEP pre-parse success entities={parseReport.EntityCount}, types={parseReport.DistinctEntityTypes.Count}, surfaces={parseReport.SurfaceEntityCount}, solids={parseReport.SolidEntityCount}");
            }
            await EnsureInitializedAsync();

            if (version != _renderVersion)
            {
                return;
            }

            var payload = JsonSerializer.Serialize(Convert.ToBase64String(stepBytes));
            var metadata = JsonSerializer.Serialize(new
            {
                fileName = sourceFileName,
                sourcePath = sourcePath,
                extension = detection.NormalizedExtension,
                fileType = detection.FileType.ToString().ToLowerInvariant()
            });

            var resultRaw = await _webView.CoreWebView2.ExecuteScriptAsync($"window.renderModelFromBase64({payload}, {metadata});");
            var result = ParseScriptResult(resultRaw);
            if (!result.Ok)
            {
                Trace.WriteLine($"[StepPreview] Renderer parse failed stage={result.Stage}, error={result.Error}");
                var message = BuildStatusMessage(result);
                ShowError(message);
                RecordDiagnostics(false, result.Error, "render", message, $"render:{result.Stage}", stepBytes.LongLength, sourceFileName, sourcePath, $"parser={result.Parser}; meshes={result.MeshCount}; triangles={result.TriangleCount}");
                return;
            }

            Trace.WriteLine($"[StepPreview] Render success stage={result.Stage}, parser={result.Parser}, meshes={result.MeshCount}, triangles={result.TriangleCount}");
            _webView.Visible = true;
            _statusLabel.Visible = false;
            RecordDiagnostics(true, string.Empty, string.Empty, "STEP preview render succeeded.", "step-preview", stepBytes.LongLength, sourceFileName, sourcePath, $"type={detection.FileType}; parser={result.Parser}; meshes={result.MeshCount}; triangles={result.TriangleCount}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[StepPreview] Render pipeline exception error={ex}");
            const string message = "WebView2 runtime unavailable for STEP preview";
            ShowError(message);
            RecordDiagnostics(false, "webview-runtime-unavailable", "runtime", message, "step-preview", stepBytes.LongLength, sourceFileName, sourcePath, ex.ToString());
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
            await _webView.CoreWebView2.ExecuteScriptAsync("window.resetViewerState?.();");
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
            await _webView.CoreWebView2.ExecuteScriptAsync("window.resetViewerState?.();");
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
                return (false, "empty-result", "deserialize-result", 0, 0, string.Empty);
            }

            return parsed.ok
                ? (true, string.Empty, parsed.stage ?? string.Empty, parsed.meshCount, parsed.triangleCount, parsed.parser ?? string.Empty)
                : (false, string.IsNullOrWhiteSpace(parsed.error) ? "parse-failed" : parsed.error, parsed.stage ?? string.Empty, parsed.meshCount, parsed.triangleCount, parsed.parser ?? string.Empty);
        }
        catch
        {
            var normalized = resultRaw.Trim('"').Trim();
            if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase))
            {
                return (true, string.Empty, "legacy-result", 0, 0, string.Empty);
            }

            return (false, string.IsNullOrWhiteSpace(normalized) ? "parse-failed" : normalized, "legacy-result", 0, 0, string.Empty);
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
            "module-load-failed" => "STEP parser could not be loaded. Verify internet access to OpenCASCADE runtime assets",
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

    private void RecordDiagnostics(bool isSuccess, string errorCode, string category, string message, string source, long fileSizeBytes, string fileName, string sourcePath, string details)
    {
        _diagnosticsLog?.RecordAttempt(
            fileName: fileName,
            filePath: sourcePath,
            fileSizeBytes: fileSizeBytes,
            isSuccess: isSuccess,
            errorCode: errorCode,
            failureCategory: category,
            message: message,
            diagnosticDetails: details,
            stackTrace: isSuccess ? string.Empty : StepParsingDiagnosticsLog.BuildCallSiteTrace(),
            source: source);
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

        await _webView.CoreWebView2.ExecuteScriptAsync("window.resizeRenderer?.();");
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
                    _webView.CoreWebView2.ExecuteScriptAsync("window.disposeViewer?.();").Wait(1500);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        base.Dispose(disposing);
    }

    private sealed record StepRenderResult(bool ok, string? error, string? stage, int meshCount = 0, int triangleCount = 0, string? parser = null);

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

    function result(ok, stage, error) {
      return { ok, stage, error: error || '' };
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
        return result(false, 'validate-content', 'invalid-step-header');
      }

      if (!sample.includes('HEADER;') || !sample.includes('DATA;')) {
        return result(false, 'validate-content', 'invalid-step-body');
      }

      return result(true, 'validate-content', '');
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

    async function renderModelFromBase64(base64, metadata) {
      if (!base64 || base64.length === 0) {
        clearScene();
        return result(false, 'load-bytes', 'missing-file-data');
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
            return result(false, 'load-parser', 'module-load-failed');
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
            return result(false, 'load-parser', 'module-load-failed');
          }

          const readers = getOcctReaders(occt, fileType);
          if (!readers || readers.length === 0) {
            clearScene();
            return result(false, 'select-parser', 'parser-not-available');
          }

          const parsed = parseOcctWithReaders(bytes, readers);
          if (!parsed) {
            clearScene();
            return result(false, 'parse-cad', fileType === 'sldprt' ? 'sldprt-not-supported' : 'corrupted-or-invalid-step');
          }

          const occtMeshData = buildMeshGroup(parsed || {});
          meshData = { group: occtMeshData.group, triangleCount: occtMeshData.triangleCount, parser: `occt-${fileType}` };
        } else {
          clearScene();
          return result(false, 'validate-format', 'unsupported-format');
        }

        if (meshData.group.children.length === 0) {
          clearScene();
          return result(false, 'load-geometry', 'empty-geometry');
        }

        clearScene();
        activeMeshRoot = meshData.group;
        scene.add(activeMeshRoot);
        fitCameraToObject(activeMeshRoot);
        return {
          ok: true,
          stage: 'display-model',
          error: '',
          meshCount: meshData.group.children.length,
          triangleCount: meshData.triangleCount,
          parser: meshData.parser
        };
      } catch (err) {
        clearScene();
        const parseError = (err && err.message) ? err.message : 'corrupted-or-invalid-step';
        return result(false, 'parse-step', parseError);
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

    window.renderModelFromBase64 = renderModelFromBase64;
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
  </script>
</body>
</html>
""";
    }
}
