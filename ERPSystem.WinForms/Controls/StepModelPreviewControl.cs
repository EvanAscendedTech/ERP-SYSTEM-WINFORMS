using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Controls;

public sealed class StepModelPreviewControl : UserControl
{
    private readonly WebView2 _webView;
    private readonly Label _statusLabel;
    private byte[] _stepBytes = Array.Empty<byte>();
    private string _sourceFileName = string.Empty;
    private string _sourcePath = string.Empty;
    private bool _initialized;
    private int _renderVersion;
    private TaskCompletionSource<bool>? _navigationReady;
    private static readonly string HtmlShell = BuildHtmlShell();

    public StepModelPreviewControl()
    {
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
        _stepBytes = stepBytes ?? Array.Empty<byte>();
        _sourceFileName = fileName ?? string.Empty;
        _sourcePath = sourcePath ?? string.Empty;
        var version = Interlocked.Increment(ref _renderVersion);
        _ = LoadStepInternalAsync(version);
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

    private async Task LoadStepInternalAsync(int version)
    {
        if (_stepBytes.Length == 0)
        {
            ShowError("STEP model unavailable (missing file data)");
            return;
        }

        var candidateName = !string.IsNullOrWhiteSpace(_sourceFileName)
            ? _sourceFileName
            : _sourcePath;
        var extension = Path.GetExtension(candidateName);
        if (!extension.Equals(".step", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".stp", StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Unsupported 3D format. Expected .step or .stp");
            return;
        }

        _statusLabel.Text = "Parsing STEP geometry...";
        _statusLabel.Visible = true;

        try
        {
            await EnsureInitializedAsync();

            if (version != _renderVersion)
            {
                return;
            }

            var payload = JsonSerializer.Serialize(Convert.ToBase64String(_stepBytes));
            var metadata = JsonSerializer.Serialize(new
            {
                fileName = _sourceFileName,
                sourcePath = _sourcePath,
                extension
            });

            var resultRaw = await _webView.CoreWebView2.ExecuteScriptAsync($"window.renderStepFromBase64({payload}, {metadata});");
            var result = ParseScriptResult(resultRaw);
            if (!result.Ok)
            {
                ShowError(BuildStatusMessage(result));
                return;
            }

            _webView.Visible = true;
            _statusLabel.Visible = false;
        }
        catch
        {
            ShowError("WebView2 runtime unavailable for STEP preview");
        }
    }

    private (bool Ok, string Error) ParseScriptResult(string resultRaw)
    {
        try
        {
            var parsed = DeserializeStepRenderResult(resultRaw);
            if (parsed is null)
            {
                return (false, "empty-result");
            }

            return parsed.ok
                ? (true, string.Empty)
                : (false, string.IsNullOrWhiteSpace(parsed.error) ? "parse-failed" : parsed.error);
        }
        catch
        {
            var normalized = resultRaw.Trim('"').Trim();
            if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase))
            {
                return (true, string.Empty);
            }

            return (false, string.IsNullOrWhiteSpace(normalized) ? "parse-failed" : normalized);
        }
    }

    private static string BuildStatusMessage((bool Ok, string Error) result)
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
            "empty-geometry" => "STEP file parsed, but no renderable geometry was produced",
            "parse-binary-failed" => "Failed to parse STEP binary payload",
            "parse-text-failed" => "Failed to parse STEP text payload",
            "corrupted-or-invalid-step" => "STEP parser rejected the file as corrupted or invalid",
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
        if (_stepBytes.Length == 0)
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
        enlargedViewer.LoadStep(_stepBytes, _sourceFileName, _sourcePath);

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

    async function renderStepFromBase64(base64, metadata) {
      if (!base64 || base64.length === 0) {
        clearScene();
        return result(false, 'load-bytes', 'missing-file-data');
      }

      try {
        const ext = (metadata && metadata.extension ? metadata.extension : '').toLowerCase();
        if (ext !== '.step' && ext !== '.stp') {
          clearScene();
          return result(false, 'validate-format', 'unsupported-format');
        }

        const bytes = base64ToUint8Array(base64);
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
        const meshData = buildMeshGroup(parsedResult.parsed || {});
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
          parser: parsedResult.parser
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

    window.renderStepFromBase64 = renderStepFromBase64;
    window.resizeRenderer = resizeRenderer;

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
