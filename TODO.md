# SkiaGameRendering - Platform Support TODO

This document tracks which framework/platform/backend combinations have been proven to work with SkiaGameRendering.

## Platform Matrix

| Framework       | Version | Backend   | Platform | Sample Project              | Status      | Notes |
|-----------------|---------|-----------|----------|-----------------------------|-------------|-------|
| MonoGame        | 3.8.4   | DesktopGL | Desktop  | samples/Sample.MonoGame.DesktopGL/  | Working     | SkiaGlBackend. Cross-platform (Windows, Linux, macOS). |
| MonoGame        | 3.8.4   | WindowsDX | Desktop  | samples/Sample.MonoGame.WindowsDX/  | Working     | SkiaAngleBackend. Windows only. Uses ANGLE + D3D11.1 SwapDeviceContextState. |
| MonoGame        | 3.8.5   | DirectX   | Desktop  | —                           | Not started | |
| MonoGame        | 3.8.5   | Vulkan    | Desktop  | —                           | Not started | |
| KNI             | —       | DesktopGL | Desktop  | —                           | Not started | |
| KNI             | —       | DirectX   | Desktop  | —                           | Not started | |
| KNI             | —       | —         | Android  | —                           | Not started | |
| KNI             | 4.3.9001 (stock) | WebGL2 | Web | samples/Sample.Kni.WebGL/ | Production candidate | Option D implemented; Chrome/Edge/Firefox hardware acceptance measurements remain release gates. |
| raylib          | 8.0.0 (Raylib-cs) | rlgl (OGL) | Desktop | samples/Sample.Raylib/ | Working (Windows + Linux) | SkiaRaylibRenderTarget2D on Core.OGL, second WGL (Windows) or GLX (Linux) context shares rlgl's GL namespace. Linux verified under WSLg (X11/GLX, Mesa llvmpipe). macOS not implemented. |

## Architecture

The library uses a backend abstraction (`SkiaBackend` base class) so each graphics API gets its own implementation. `SkiaRenderer.Initialize(GraphicsDevice)` auto-detects the correct backend at runtime.

- **`SkiaGlBackend`** — OpenGL via SDL context sharing (DesktopGL)
- **`SkiaAngleBackend`** — D3D11 via ANGLE's GL ES translation (WindowsDX, Windows only)
- **`SkiaWebGlBackend`** — separate WebGL2 canvas/context, composited via canvas-to-texture upload (KNI/Blazor)

Because MonoGame.Framework.DesktopGL and MonoGame.Framework.WindowsDX are separate NuGet packages that can't coexist, each backend has its own library project. Core source files are shared via linked includes:

- `src/SkiaGameRendering/` — DesktopGL library (core + GL backend)
- `src/SkiaGameRendering.Core.OGL/` — engine-agnostic raw-GL/Skia FBO interop shared by GL-based backends
- `src/SkiaGameRendering.WindowsDX/` — WindowsDX library (shares core files + ANGLE backend)
- `src/SkiaGameRendering.Kni.WebGL/` — KNI/Blazor library (shares core files + WebGL backend)

**raylib is the first non-MonoGame engine**, and the first consumer of `Core.OGL` from outside the
MonoGame family (tracked in [issue #3](https://github.com/vchelaru/SkiaGameRendering/issues/3)).
It does *not* derive from `SkiaBackend`/`SkiaTarget` — those are typed directly to MonoGame's
`Texture2D`, and issue #3 explicitly rules out a MonoGame dependency for a second engine. Instead
`src/SkiaGameRendering.Raylib/` is a hand-rolled sibling typed to raylib's own `Texture2D`
throughout, reusing `Core.OGL`'s FBO/Skia-surface interop as-is:

- **`SkiaRaylibRenderTarget2D`** — public Begin/Canvas/End API mirroring `SkiaRenderTarget2D`'s shape, `.Texture` returns a raylib `Texture2D` usable directly with `Raylib.DrawTexture*`.
- **`SkiaRaylibContext`** / **`IPlatformGlContext`** (`Wgl`, `Glx`) — the shared-context helper the spike (see below) proved necessary: rlgl (raylib's GL layer) and Skia both issue raw GL calls, and sharing raylib's single context between them corrupts rlgl's own rendering. A second context sharing raylib's GL object namespace (same trick `SkiaGlBackend` uses via SDL) fixes it. raylib statically links GLFW and doesn't export context-creation entry points, so this goes through the OS's raw windowing API directly rather than GLFW: raw Win32/WGL (`Wgl.cs`) on Windows, raw X11/GLX (`Glx.cs`) on Linux - `SkiaRaylibContext` picks one at runtime via `OperatingSystem.IsWindows()`/`IsLinux()`. `Glx.cs` needed one deviation from the WGL pattern: GLX has no "current pixel format of this drawable" concept the way an HDC does, so it reads the FBConfig id off raylib's own live context (`glXQueryContext`) and re-resolves the matching `GLXFBConfig` rather than re-deriving one from scratch, and it takes the drawable from `glXGetCurrentDrawable()` rather than from `Raylib.GetWindowHandle()`'s raw X11 Window - GLFW's own GLX context uses a separate `GLXWindow` it creates internally via `glXCreateWindow`, and passing the plain X11 Window to `glXMakeCurrent` produced a `BadDrawable` X error, confirmed empirically under WSLg. Also needed `SkiaSharp.NativeAssets.Linux` added to `SkiaGameRendering.Raylib.csproj` - the main `SkiaSharp` package only carries Windows/macOS native assets implicitly, and without it `GRContext.CreateGl()` throws `DllNotFoundException` for `libSkiaSharp.so` at runtime on Linux even though the build itself succeeds.
- The vertical flip between Skia's top-left-origin rendering and raylib's bottom-left texture sampling is baked into the surface at creation time (`GRSurfaceOrigin.BottomLeft`, via a new optional parameter on `GlSkiaSurfaceFactory.CreateSurface` that defaults to `TopLeft` for existing MonoGame callers) rather than left to the caller to flip per-draw.

This was de-risked first as a throwaway spike (`spikes/raylib-ogl-v0/`, since removed - its finding is preserved in issue #3's spike comment and carried into `Wgl.cs`'s doc comment above).

## Known Issues / Cleanup

- **ANGLE DLL packaging**: Currently falls back to Edge WebView's system ANGLE DLLs. The Silk.NET.OpenGLES.ANGLE.Native NuGet package ships 32-bit DLLs mislabeled as x64. Need a reliable x64 ANGLE source — either a correct NuGet package, a manual ANGLE build, or direct bundling.
- **SetData workaround for lazy texture creation**: MonoGame WindowsDX doesn't allocate the D3D11 GPU resource in the Texture2D constructor. The ANGLE backend calls `SetData(new byte[w*h*4])` to force allocation, which is wasteful. Need a cheaper way to trigger D3D11 resource creation.
- **Reflection fragility**: Desktop backends rely on MonoGame internal field names (`_d3dDevice`, `_d3dContext`, `_texture`, SDL internals). These may change between MonoGame versions. The WindowsDX backend additionally reflects into SharpDX types (`Device1`, `DeviceContext1`). If MonoGame moves away from SharpDX, the ANGLE backend needs updating. The KNI WebGL backend similarly reflects into KNI's `ConcreteTexture2D`/`ConcreteGraphicsContext` internals (`WebGlCanvasUpload.cs`'s `KniWebGlInternals`) to reach the native `WebGLTexture`/rendering-context handles KNI doesn't expose publicly — see issue #2.
- **glFinish performance**: The ANGLE backend calls `glFinish()` per renderable for GPU sync. Could potentially be relaxed to `glFlush()` if D3D11's internal synchronization is sufficient.

## Open Questions

- **MonoGame 3.8.5 Vulkan**: DX11 or DX12? Need to decide which DirectX version the Vulkan row targets.
- **KNI versions**: Which KNI package versions to target?

## Next Steps

1. Run and archive the hardware benchmark matrix in `benchmarks/Benchmarks.WebGL/` — tracked in [issue #5](https://github.com/vchelaru/SkiaGameRendering/issues/5).
2. Address the desktop ANGLE DLL and lazy-allocation issues above.
3. Split per-graphics-API core libraries out of the per-engine adapters so a new engine (raylib) can reuse the GL/Skia interop instead of duplicating it — tracked in [issue #3](https://github.com/vchelaru/SkiaGameRendering/issues/3). The OGL split (`src/SkiaGameRendering.Core.OGL/`) landed via [#4](https://github.com/vchelaru/SkiaGameRendering/pull/4), and `src/SkiaGameRendering.Raylib/` now proves it against a real second (non-MonoGame) engine on both Windows and Linux (`Glx.cs`, verified under WSLg — tracked in [issue #9](https://github.com/vchelaru/SkiaGameRendering/issues/9)). Issue #3 is not fully closed yet: macOS raylib support is unimplemented, and the ANGLE/WebGL backends are still intentionally unmigrated (step 3/4 of the issue's sequencing).

[Issue #2](https://github.com/vchelaru/SkiaGameRendering/issues/2) (closed) tracked the KNI upstream dependency: kniEngine/kni#2669 shipped in KNI v4.3.9001, so the KNI source patch/fork is gone (moved to stock NuGet). The one piece #2669 didn't cover — a public accessor for the current WebGL rendering context — is bridged with reflection (`WebGlCanvasUpload.cs`); pitching KNI a follow-up PR for it is tracked in [issue #13](https://github.com/vchelaru/SkiaGameRendering/issues/13). [Issue #14](https://github.com/vchelaru/SkiaGameRendering/issues/14) (closed) tracked an apparent 4.3.9001 startup crash that turned out not to be a KNI bug: `nkast.Wasm.*`'s dependency version bumped from 8.0.11 (under KNI 4.2.9001) to 10.0.3 (under 4.3.9001), and `wwwroot/index.html`'s hardcoded `<script>` version strings hadn't been updated to match — a silent 404, not a build failure. `eng/Versions.props`'s `NkastWasmCanvasVersion` must always match whatever the pinned `KniVersion` actually depends on.
