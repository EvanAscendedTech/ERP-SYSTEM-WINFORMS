# STEP Handling Pipeline Architectural Review

## Current Integration Topology

1. `QuoteDraftForm` and `QuotesControl` identify latest `.step/.stp` attachments and call `StepModelPreviewControl.LoadStep*`.
2. `StepModelPreviewControl` validates extension and now performs a native C# pre-parse via `StepFileParser` before any renderer transition.
3. The WebView layer loads:
   - Three.js (`three.min.js`)
   - Orbit controls
   - Open CASCADE WebAssembly bridge (`occt-import-js`)
4. Browser script parses STEP with OCCT and builds renderable triangle meshes.

## Dependency Validation

- `Microsoft.Web.WebView2` is present and is the host for rendering and parser execution.
- OCCT parser runtime is loaded from `occt-import-js` CDN and used through `ReadStepFile`.
- Three.js is used only after successful parse and mesh extraction.

## Risks Found and Mitigations

### 1) Missing early parser diagnostics
- **Risk:** prior flow only surfaced high-level UI errors.
- **Mitigation:** added `StepFileParser` pre-parse and Trace logging for structural validation, entity inventory, and parser outcomes.

### 2) Unsupported or malformed STEP content not isolated early
- **Risk:** bad data reached WebView parser with limited context.
- **Mitigation:** parser now verifies:
  - ISO-10303-21 header
  - `HEADER;` / `DATA;` section ordering
  - Parseable entity records
  - Presence of known surface/solid entities

### 3) UI transition timing safety
- **Risk:** previewer might display before scene is safe.
- **Mitigation:** control keeps status visible and only shows WebView after successful parse + mesh construction result (`ok: true`).

## Geometry Coverage

`StepFileParser` recognizes representative standard entities for solids and surfaces, including:
- **Surfaces:** `ADVANCED_FACE`, `FACE_SURFACE`, `B_SPLINE_SURFACE*`, `PLANE`, `CYLINDRICAL_SURFACE`, etc.
- **Solids:** `MANIFOLD_SOLID_BREP`, `BREP_WITH_VOIDS`, `FACETED_BREP`, `ADVANCED_BREP_SHAPE_REPRESENTATION`, etc.

OCCT remains the authoritative geometry interpreter for final tessellation and scene construction.

## Logging Strategy

Added `Trace.WriteLine` checkpoints in `StepModelPreviewControl`:
- parse start (filename/byte size)
- pre-parse success/failure details
- renderer parse failure with stage+error
- render success with parser mode and mesh/triangle counts
- pipeline exceptions

This isolates whether failures originate from:
- invalid STEP structure
- unsupported entity profile
- WebView/runtime initialization
- OCCT parser load
- OCCT parse failure
- empty geometry emission
