# SkiaGameRendering - Platform Support TODO

This document tracks which framework/platform/backend combinations have been proven to work with SkiaGameRendering.

## Platform Matrix

| Framework       | Version | Backend   | Platform | Sample Project              | Status      | Notes |
|-----------------|---------|-----------|----------|-----------------------------|-------------|-------|
| MonoGame        | 3.8.4   | DesktopGL | Desktop  | samples/Sample.MonoGame.DesktopGL/  | Working     | See `docs/desktop/quickstart.md`. Cross-platform (Windows, Linux, macOS). |
| MonoGame        | 3.8.4   | WindowsDX | Desktop  | samples/Sample.MonoGame.WindowsDX/  | Working     | See `docs/desktop/quickstart.md`. Windows only. |
| MonoGame        | 3.8.5   | DirectX   | Desktop  | —                           | Not started | |
| MonoGame        | 3.8.5   | Vulkan    | Desktop  | —                           | Not started | |
| KNI             | 4.3.9001 (stock) | DesktopGL | Desktop  | samples/Sample.Kni.DesktopGL/ | Working | See `docs/desktop/quickstart.md`. Cross-platform (Windows, Linux, macOS). |
| KNI             | 4.3.9001 (stock) | WindowsDX | Desktop  | samples/Sample.Kni.WindowsDX/ | Working | See `docs/desktop/quickstart.md`. Windows only. |
| KNI             | —       | —         | Android  | —                           | Not started | |
| KNI             | 4.3.9001 (stock) | WebGL2 | Web | samples/Sample.Kni.WebGL/ | Production candidate | See `docs/webgl/quickstart.md`. Chrome/Edge/Firefox hardware acceptance measurements remain release gates. |
| raylib          | 8.0.0 (Raylib-cs) | rlgl (OGL) | Desktop | samples/Sample.Raylib/ | Working (Windows + Linux) | See `docs/raylib/quickstart.md`. macOS not implemented. |

## Architecture

The library uses a backend abstraction (`SkiaBackend` base class) so each graphics API gets its
own implementation, documented per platform in `docs/`:

- [`docs/desktop/quickstart.md`](docs/desktop/quickstart.md) — MonoGame/KNI DesktopGL and
  WindowsDX (`SkiaGlBackend`, `SkiaKniGlBackend`, `SkiaAngleBackend`, `SkiaKniAngleBackend`),
  project layout, and known limitations.
- [`docs/webgl/quickstart.md`](docs/webgl/quickstart.md) — KNI WebGL/Blazor (`SkiaWebGlBackend`).
- [`docs/raylib/quickstart.md`](docs/raylib/quickstart.md) — raylib (`SkiaRaylibRenderTarget2D`),
  the first non-MonoGame engine and the first consumer of `Core.OGL` from outside the MonoGame
  family (tracked in [issue #3](https://github.com/vchelaru/SkiaGameRendering/issues/3); it does
  *not* derive from `SkiaBackend`/`SkiaTarget`, deliberately, since those are typed to MonoGame's
  `Texture2D`).

This was de-risked first as a throwaway spike (`spikes/raylib-ogl-v0/`, since removed — its
finding is preserved in issue #3's spike comment and carried into `Wgl.cs`'s doc comment).

## Known Issues / Cleanup

- **ANGLE DLL packaging**: currently falls back to Edge WebView's system ANGLE DLLs — the
  Silk.NET.OpenGLES.ANGLE.Native NuGet package ships 32-bit DLLs mislabeled as x64. Need a reliable
  x64 ANGLE source — either a correct NuGet package, a manual ANGLE build, or direct bundling. See
  `docs/desktop/quickstart.md`'s Known limitations.
- **SetData workaround for lazy texture creation**: MonoGame WindowsDX's ANGLE backend forces D3D11
  resource allocation with a wasteful `SetData(new byte[w*h*4])` call (see `SkiaAngleBackend.md`).
  Need a cheaper trigger.
- **glFinish performance**: the ANGLE backends call `glFinish()` per renderable for GPU sync.
  Could potentially be relaxed to `glFlush()` if D3D11's internal synchronization is sufficient —
  unverified.

## Open Questions

- **MonoGame 3.8.5 Vulkan**: DX11 or DX12? Need to decide which DirectX version the Vulkan row targets.
- **KNI versions**: Which KNI package versions to target?

## Next Steps

1. Run and archive the hardware benchmark matrix in `benchmarks/Benchmarks.WebGL/` — tracked in [issue #5](https://github.com/vchelaru/SkiaGameRendering/issues/5).
2. Address the desktop ANGLE DLL and lazy-allocation issues above.
3. Split per-graphics-API core libraries out of the per-engine adapters so a new engine (raylib) can reuse the GL/Skia interop instead of duplicating it — tracked in [issue #3](https://github.com/vchelaru/SkiaGameRendering/issues/3). The OGL split (`src/SkiaGameRendering.Core.OGL/`) landed via [#4](https://github.com/vchelaru/SkiaGameRendering/pull/4), and `src/SkiaGameRendering.Raylib/` now proves it against a real second (non-MonoGame) engine on both Windows and Linux (`Glx.cs`, verified under WSLg — tracked in [issue #9](https://github.com/vchelaru/SkiaGameRendering/issues/9)). Issue #3 is not fully closed yet: macOS raylib support is unimplemented, and step 3 (`SkiaAngleBackend` → `Core.ANGLE`, now also reused by `src/SkiaGameRendering.Kni.WindowsDX/`) has landed, leaving step 4 (`SkiaWebGlBackend` → `Core.WebGL`) as the only remaining unmigrated backend.

[Issue #2](https://github.com/vchelaru/SkiaGameRendering/issues/2) (closed) tracked the KNI upstream dependency: kniEngine/kni#2669 shipped in KNI v4.3.9001, so the KNI source patch/fork is gone (moved to stock NuGet). The one piece #2669 didn't cover — a public accessor for the current WebGL rendering context — is bridged with reflection (`WebGlCanvasUpload.cs`); pitching KNI a follow-up PR for it is tracked in [issue #13](https://github.com/vchelaru/SkiaGameRendering/issues/13). [Issue #14](https://github.com/vchelaru/SkiaGameRendering/issues/14) (closed) tracked an apparent 4.3.9001 startup crash that turned out not to be a KNI bug: `nkast.Wasm.*`'s dependency version bumped from 8.0.11 (under KNI 4.2.9001) to 10.0.3 (under 4.3.9001), and `wwwroot/index.html`'s hardcoded `<script>` version strings hadn't been updated to match — a silent 404, not a build failure. `eng/Versions.props`'s `NkastWasmCanvasVersion` must always match whatever the pinned `KniVersion` actually depends on.
