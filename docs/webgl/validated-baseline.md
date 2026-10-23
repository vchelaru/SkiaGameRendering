# Validated WebGL baseline

Updated: 2026-09-05

| Component | Pin |
|---|---|
| .NET target | net8.0 |
| KNI package | stock NuGet, no fork/patch (`nkast.Kni.Platform.Blazor.GL`, `nkast.Xna.Framework.Graphics`) |
| KNI package baseline | 4.3.9001 |
| nkast.Wasm.Canvas | 10.0.3 |
| SkiaSharp | 4.153.1 |
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
  readback. Per the "if a mandatory browser misses budget" policy below, this triggered the
  Option A gate; `SkiaDirectCanvas` is Option A (see below). `SkiaRenderTarget2D` still takes
  the upload path and still misses budget on Firefox.
- **Safari: still Tier 2, untested.** No Mac hardware was available for this run.

## Option A: `SkiaDirectCanvas`

Option A (shared context, [WebGL-KNI-Integration.md](../../WebGL-KNI-Integration.md) section 3) shipped as
`SkiaDirectCanvas` on KNI WebGL. KNI's own `WebGL2RenderingContext` is registered with SkiaSharp's
Emscripten GL runtime, so Skia draws into the back buffer or a bound `RenderTarget2D` with no upload
and no second canvas. KNI is not patched: the state cache it keeps is reset by reflection
(`KniStateInvalidation`), which `tests/Tests.Kni.WebGL.Reflection` pins.

- **Pixels:** `tests/Tests.Kni.WebGL/tests/scene-direct.spec.js` reads KNI's canvas back after KNI, Skia,
  KNI, Skia, KNI draws, on the back buffer, a render target and a 4x multisampled one, and checks the
  Scene's circle and drop. It passes on Chromium at DPR 1, 1.25, 1.5 and 2, and on Firefox 158 (real
  GPU, below), and CI also runs it on Playwright's Firefox and WebKit. WebKit did not run locally: the
  existing WebKit scene test does not start the sample on this Windows machine either.
- **Cost, Firefox 158, Intel Arc A750 (ANGLE/D3D11)**, from `benchmarks/Benchmarks.WebGLOptionA` (headless,
  300 frames after 60 warm-up): CPU time per frame for the state reset, a Skia draw and a KNI draw.
  Firefox rounds timers to 1 ms, so read these as +/- 1 ms. Compare the 32 ms+ of the upload path.

  | Resolution | p50 | p95 |
  |---|---:|---:|
  | 1920x1080 | 1 ms | 2 ms |
  | 2560x1440 | 1 ms | 2 ms |
  | 3840x2160 | 2 ms | 4 ms |

- **Not measured:** the production backend's frame time (the spike measures the same handoff), GPU time
  (Firefox has no timer query), Safari.
- **Known quirk:** Skia's first pass raises one harmless `INVALID_OPERATION` on Firefox, from its color
  read format capability query on the default framebuffer. `endDirectDraw` clears the queue after every
  pass, and the tests check that no more arrive.
