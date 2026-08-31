# Validated WebGL baseline

Updated: 2026-08-31

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

No hardware performance result is asserted by this file yet. Chrome, Edge, and Firefox remain release candidates rather than declared Tier 1 until benchmark JSON reports are checked in from representative hardware. Safari remains Tier 2.

Option A is intentionally not implemented: no measured mandatory-browser failure has triggered its gate.
