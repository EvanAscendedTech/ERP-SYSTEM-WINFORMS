# Offline STEP ➜ GLB Preview Pipeline (Fresh Implementation)

This implementation removes repository-backed and parser-heavy 3D preview dependencies and replaces them with a local, offline-only flow.

## Pipeline stages

1. **`step-validate`**
   - Input must be `.step` / `.stp`.
   - Validate payload is non-empty.
   - Validate STEP header contains `ISO-10303-21`.
   - Emit diagnostics entries tagged with `[step-validate]`.

2. **`step-convert`**
   - Compute SHA-256 hash of STEP payload.
   - Resolve local converter executable (`ERP_STEP2GLB_PATH` or bundled `Tools/...`).
   - Convert STEP to GLB with no network calls.
   - Cache resulting GLB on local disk by STEP hash.
   - Emit diagnostics entries tagged with `[step-convert]`.

3. **`glb-render`**
   - Load local HTML viewer inside WebView2 (`Assets/gltf-viewer.html`).
   - Send GLB payload as base64 to the page.
   - Render and capture viewer acknowledgements/errors.
   - Emit diagnostics entries tagged with `[glb-render]`.

## Removed prior dependencies from the active path

- No `QuoteRepository` dependency for GLB caching.
- No STEP entity/schema parser dependency during upload validation.
- No broad multi-format 3D detector requirement for preview flow.

## New dependency model

- **Required:** local STEP bytes, local converter executable, local filesystem cache, local WebView2 runtime.
- **Not required:** remote services, online APIs, server-side geometry pipelines.

## Runtime behavior summary

- First preview of a STEP file performs validation + conversion + render.
- Repeated previews of the same STEP bytes hit hash cache and skip conversion.
- Diagnostics always identify stage names explicitly: `step-validate`, `step-convert`, `glb-render`.
