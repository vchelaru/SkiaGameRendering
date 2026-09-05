# Validated WebGL baseline

Updated: 2026-09-05

| Component | Pin |
|---|---|
| .NET target | net8.0 |
| KNI package | stock NuGet, no fork/patch (`nkast.Kni.Platform.Blazor.GL`, `nkast.Xna.Framework.Graphics`) |
| KNI package baseline | 4.3.9001 |
| nkast.Wasm.Canvas | 10.0.3 |
| SkiaSharp | 3.119.4 |
| Gum.SkiaSharp | 2026.5.31.1 |
| Graphics profile | HiDef / WebGL2 |
| Texture format | RGBA8 / `SurfaceFormat.Color` / `SKColorType.Rgba8888` |

Debug and Release native-WASM builds are validated on Windows. A package-consumer Release publish with trimming and full WASM AOT succeeds. Core lifecycle tests pass. `webgl-functional` is green in CI end to end (18/18): Chromium at DPR 1, 1.25, 1.5, and 2, Firefox, and WebKit, all covering context loss, page reload, input, backend recreation, and both canvas upload modes (run 33378505569). Firefox needed two CI-only fixes with no code changes: `webgl.force-enabled` (headless Firefox disables WebGL2 by gfx-config default) and running under `xvfb-run` (Firefox's GL stack needs a real/virtual X display for GLX; Chromium's bundled software renderer doesn't).

Hardware acceptance measured 2026-09-05 on an RTX 5060 Laptop GPU (ANGLE/D3D11); see
`performance-results.md` for the full matrix and raw JSON.

- **Chrome: Tier 1.** Every production upload path stays inside budget (< 500 µs upload CPU,
  < 1 ms upload GPU) at 1080p/1440p/4K.
- **Edge: Tier 1.** Same Chromium/ANGLE stack as Chrome; numbers track closely.
- **Firefox: fails budget, mandatory browser.** Every upload path — including
  `OffscreenCanvas + transferToImageBitmap`, answering the issue's open sub-question — misses
  budget by roughly 60-300x (32 ms+ at 1080p vs. a 500 µs target), consistent with an internal CPU
  readback. Per the "if a mandatory browser misses budget" policy below, this **triggers the
  Option A gate**: Option A is now required before Firefox can be declared Tier 1.
- **Safari: still Tier 2, untested.** No Mac hardware was available for this run.

Option A (shared-context — see [WebGL-KNI-Integration.md](../../WebGL-KNI-Integration.md), section 3)
is not yet implemented. Its gate has now been triggered by a measured Firefox failure (above); the
work itself — a `GraphicsDevice.InvalidateStateCache()`-equivalent in KNI, and reconciling Skia's
Emscripten WASM build with KNI's JS-interop WebGL context — is unstarted and tracked separately.
