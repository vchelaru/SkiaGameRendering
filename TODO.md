# SkiaGameRendering - Platform Support TODO

This document tracks which framework/platform/backend combinations have been proven to work with SkiaGameRendering.

## Platform Matrix

| Framework       | Version | Backend   | Platform | Sample Project              | Status      | Notes |
|-----------------|---------|-----------|----------|-----------------------------|-------------|-------|
| MonoGame        | 3.8.5.1 | DesktopGL | Desktop  | samples/Sample.MonoGame.DesktopGL/  | Working     | See `docs/desktop/quickstart.md`. Cross-platform (Windows, Linux, macOS). macOS verified on Apple silicon by running the sample (MonoGame `--smoke-test` passes, KNI renders the scene); the DesktopGL test projects abort there because SDL needs the main thread. |
| MonoGame        | 3.8.5.1 | WindowsDX | Desktop  | samples/Sample.MonoGame.WindowsDX/  | Working     | See `docs/desktop/quickstart.md`. Windows only. |
| MonoGame        | 3.8.6 | WindowsDX12 | Desktop | samples/Sample.MonoGame.WindowsDX12/ | Working | `GetNativeHandles()` and `RenderTarget2D.FromNativeHandle()` (MonoGame#9536, #9535) supply the device/queue and a way to hand Skia's resource back. The game also references `MonoGame.Runtime.Windows.DX12`. See `SkiaGameRendering-Notes.md` section 9. |
| MonoGame        | 3.8.6 | DesktopVK | Desktop  | samples/Sample.MonoGame.DesktopVK/ | Working | Not published (blocked on MonoGame#9588, see `CLAUDE.md`). Windows verified; Linux unrun. The game also references `MonoGame.Runtime.Windows.Vulkan` or `MonoGame.Runtime.Linux.Vulkan`. |
| KNI             | 4.3.9001 (stock) | DesktopGL | Desktop  | samples/Sample.Kni.DesktopGL/ | Working | See `docs/desktop/quickstart.md`. Cross-platform (Windows, Linux, macOS). macOS verified on Apple silicon by running the sample (MonoGame `--smoke-test` passes, KNI renders the scene); the DesktopGL test projects abort there because SDL needs the main thread. |
| KNI             | 4.3.9001 (stock) | WindowsDX | Desktop  | samples/Sample.Kni.WindowsDX/ | Working | See `docs/desktop/quickstart.md`. Windows only. |
| KNI             | 4.3.9001 (stock) | GLES | Android | samples/Sample.Kni.Android/ | Working (2 devices) | See `docs/android/quickstart.md`. Verified on a Mali-G615 phone (Android 16) and a PowerVR GE8300 phone (Android 12L), both GLES 3.2 and surfaceless. Also run on emulators, and by CI on an x86_64 SwiftShader emulator. Unverified: GLES 3.0-only drivers, the host-surface fallback, 32-bit ARM, rotation. |
| KNI             | 4.3.9001 (stock) | WebGL2 | Web | samples/Sample.Kni.WebGL/ | Working (Chrome, Edge); `SkiaRenderTarget2D` blocked on Firefox, `SkiaDirectCanvas` works there | See `docs/webgl/quickstart.md`. Chrome/Edge measured Tier 1 on physical hardware; Firefox misses the upload budget by 60-300x for `SkiaRenderTarget2D`, so use `SkiaDirectCanvas` (Option A, shipped: Skia and KNI share KNI's context, state reset by reflection) — see `docs/webgl/validated-baseline.md` and `docs/webgl/performance-results.md`. Safari (Tier 2) untested, no Mac available; local WebKit does not start the sample on Windows, CI's WebKit runs it. |
| MonoGame        | 3.8.5.1 | GLES | Android | samples/Sample.MonoGame.Android/ | Working | See `docs/android/monogame-quickstart.md`. Same `SkiaAndroidBackend` as KNI Android, so the KNI Android verification (two physical phones, emulators, CI on a SwiftShader emulator) and gaps (GLES 3.0-only drivers) carry over. |
| MonoGame        | 3.8.5.1 | GLES (EAGL) | iOS | samples/Sample.MonoGame.iOS/ | Working (CI simulator only) | See `docs/ios/quickstart.md`. Targets `net10.0-ios26.5` (Xcode 26.6 on a Mac). Verified on the CI iOS Simulator only: no physical iPhone or iPad, no GLES 2-only device. |
| KNI             | 4.3.9001 (stock) | GLES (EAGL) | iOS | samples/Sample.Kni.iOS/ | Working (CI simulator only) | See `docs/ios/kni-quickstart.md`. Targets `net10.0-ios26.5`. Same verification limits as MonoGame iOS. |
| FNA             | 26.09 (submodule) | OpenGL | Desktop | samples/Sample.Fna.OGL/ | Working | See `docs/fna/quickstart.md`. Windows, Linux, macOS via FNA3D's OpenGL driver. FNA is a submodule plus `ProjectReference`, not a package. |
| FNA             | 26.09 (submodule) | D3D11 | Desktop (Windows) | samples/Sample.Fna.WindowsDX/ | Working | See `docs/fna/quickstart.md`. FNA3D's D3D11 driver through ANGLE. FNA's SDL_GPU driver (D3D12, Vulkan, Metal) exposes no native device handles, so those are not supported. |
| FNA             | 26.09 (submodule) | WebGL2 | Web | samples/Sample.Fna.WebGL/ | Working | See `docs/fna/webgl-quickstart.md`. Needs .NET 10, `wasm-tools` and FNA's SDL2 platform backend (SDL3 cannot start under Mono's WebAssembly interpreter). Skia and FNA3D share one WebGL context. |
| Stride          | 4.4.1   | D3D11 | Desktop (Windows) | samples/Sample.Stride.D3D11/ | Working | See `docs/stride/quickstart.md`. Not a `SkiaBackend`; Stride has its own adapter. |
| Stride          | 4.4.1   | D3D12 | Desktop (Windows) | samples/Sample.Stride.D3D12/ | Working | See `docs/stride/d3d12-quickstart.md`. WARP also works. |
| Stride          | 4.4.1   | Vulkan | Desktop | samples/Sample.Stride.VK/ | Working | See `docs/stride/vulkan-quickstart.md`. Needs a Vulkan 1.3 driver. Linux GPU paths are not verified. |
| raylib          | 8.1.0 (Raylib-cs) | WebGL2 | Web | samples/Sample.Raylib.WebGL/ | Working | See `docs/raylib/webgl-quickstart.md`. Needs .NET 10 and `wasm-tools`. raylib and SkiaSharp share one Emscripten WebGL context. |
| Unity           | 6       | D3D11, D3D12, Vulkan, OpenGL | Desktop (Windows x64) | samples/Sample.Unity/ | Working (local only) | See `docs/unity/quickstart.md`. Mono and IL2CPP. Verified by the maintainer locally, never in CI. Linux is not verified. |
| Unity           | 6       | OpenGL ES 3 | Android | samples/Sample.Unity/ | Working (local only) | See `docs/unity/quickstart.md`. IL2CPP, arm64, OpenGLES3 only (not Vulkan, not OpenGLES2). |
| Unity           | 6       | Metal | Desktop (macOS) | samples/Sample.Unity/ | Working (local only) | See `docs/unity/quickstart.md`. Verified on a Mac (Apple M5 Pro, Unity 6000.6.3f1, built-in pipeline, URP 17.6 and HDRP 17.6): the smoke test, including the `SkiaDirectCanvas` area, passes in Gamma and Linear on the built-in pipeline and URP and in Linear on HDRP (HDRP needs Linear), each also with `--msaa`, and the Editor tests pass; Metal API and GPU validation (`MTL_DEBUG_LAYER=1`, `METAL_DEVICE_WRAPPER_TYPE=1`, `MTL_SHADER_VALIDATION=1`) reports nothing on the built-in, URP and HDRP players. Direct canvas refuses MSAA targets like D3D12 and Vulkan, and under URP and HDRP on Metal the camera renders into the multisampled back buffer itself, so `--msaa` refuses that too. Unrun: IL2CPP (the Mac IL2CPP module is not installed here), HDRP with its own asset MSAA mode (#367). |
| Unity           | 6       | WebGL | Web | samples/Sample.Unity/ | Not supported | Unity's Emscripten has to match the one SkiaSharp's wasm build uses (3.1.56) and nothing has been shown to work. |
| raylib          | 8.0.0 (Raylib-cs) | rlgl (OGL) | Desktop | samples/Sample.Raylib.OGL/ | Working (Windows + Linux + macOS) | See `docs/raylib/quickstart.md`. macOS (a CGL shared context) runs `SkiaRenderTarget2D` in `samples/Sample.Raylib.OGL` on Apple silicon; `SkiaDirectCanvas` is not supported there, and `Tests.Raylib.OGL` skips its window tests on macOS because GLFW needs the main thread. |
| Godot           | 4.7.2 (GodotSharp) | Vulkan (RenderingDevice) | Desktop | samples/Sample.Godot/ | Working (Windows verified, incl. clean `--gpu-validation`; Linux expected, unrun; macOS unsupported: SkiaSharp's macOS build has no Vulkan) | See `docs/godot/quickstart.md`. No reflection: `RenderingDevice.GetDriverResource` is public. Zero-copy. |
| Godot           | 4.7.2 (GodotSharp) | D3D12 (RenderingDevice) | Desktop (Windows) | samples/Sample.Godot/ | Working (Windows verified) | Same package, chosen at runtime. Not zero-copy: Godot allocates typeless D3D12 textures and Skia's D3D12 backend cannot render into those, so Skia draws into a typed resource and one GPU `CopyResource` per frame lands it in Godot's texture. See `SkiaGameRendering-Notes.md` section 11. |
| Godot           | 4.7.2 (GodotSharp) | Compatibility (opengl3) | Desktop | samples/Sample.Godot/ | Working (Windows WGL verified; Linux X11/GLX compiled from the raylib adapter's code, unrun) | Same package. No `RenderingDevice`: a second WGL/GLX context shares Godot's GL objects and Skia draws into an FBO around an `ImageTexture`'s GL texture (`RenderingServer.TextureGetNativeHandle`). RGBA8 only. Not `opengl3_angle`/`opengl3_es`/Wayland/macOS (EGL/NSOpenGL contexts). |
| Godot           | 4.7.2 (GodotSharp) | Metal (RenderingDevice) | Desktop (macOS) | samples/Sample.Godot/ | Working (Apple silicon verified; Intel unrun) | Same package. Zero-copy: Skia draws into the RD texture's `MTLTexture`; Metal has no layouts or states to hand back. Vulkan on macOS stays unsupported (SkiaSharp's macOS build has no Vulkan). |

## Direct Canvas Coverage

Which engine/backend pairs have a render target (`SkiaRenderTarget2D` or the engine's equivalent) and
which also draw straight into the current target (`SkiaDirectCanvas`).

✅ render target + direct  🟦 render target only  🟡 neither, but the engine has this API  ❌ the engine doesn't have this API  ⛔ engine has it, not supported for now

| Engine | OpenGL / GLES | D3D11 | D3D12 | Vulkan | Metal | WebGL |
|---|---|---|---|---|---|---|
| MonoGame | ✅ DesktopGL, Android, iOS | ✅ | ✅ | 🟦 (unpublished) | ❌ | ❌ |
| KNI | ✅ DesktopGL, Android, iOS | ✅ | ❌ | ❌ | ❌ | ✅ |
| FNA | ✅ | ✅ | ❌ | ❌ | ❌ | ✅ |
| raylib | ✅ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Stride | ❌ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Godot 4.7+ | 🟦 (Compat) | ❌ | 🟦 | 🟦 | 🟦 | ❌ |
| Unity 6 | ✅ Windows, Android | ✅ | ✅ | ✅ | ✅ | ⛔ |

Unity WebGL is ⛔ because Unity's Emscripten must match the one SkiaSharp's wasm build uses (3.1.56) and nothing has been shown to work.

FNA's D3D12, Vulkan and Metal are ❌ because FNA3D reaches them through its SDL_GPU driver, which
exposes no native device handles.

MonoGame's D3D12 is ✅ by reading MonoGame's native objects by offset (no MonoGame change); it is only enabled for
an `mgruntime.dll` whose layout has been verified, and `SupportsDirectCanvas` is false on any other build
(Notes section 12).

Shared code to reuse when moving a 🟦 to ✅:

- OpenGL: `GlSkiaSurfaceFactory` in `Core.OGL` and `SkiaSharedGlBackend`. An engine that derives from
  `SkiaSharedGlBackend` only overrides `CanDrawToCurrentTarget` and `GetBoundRenderTarget`.
- D3D11: `AngleSkiaSurfaceFactory.BeginDirectDraw`/`EndDirectDraw` in `Core.ANGLE`, which reads the bound
  view off the immediate context. Used by `SkiaSharedAngleBackend` and the Stride D3D11 `SkiaDirectCanvas`.
- D3D12: `D3D12SkiaSurfaceFactory.DescribeDirectTarget`/`BeginDirectDraw`/`EndDirectDraw` in `Core.D3D12`,
  which take the target's `ID3D12Resource` from the engine adapter (no bound-target query exists). The
  adapter has to put it in `RENDER_TARGET` first and submit the engine's recorded commands ahead of
  Skia's, then restore the engine's list state: `D3D12CommandListSplitter` cuts and reopens an engine's open
  command list. Used by `SkiaStrideD3D12Context.BeginDirectDraw` (which uses Stride's own `FlushInternal`) and
  `SkiaDx12Backend` (`MonoGameD3D12Native` finds the target and restores MonoGame's state).
- WebGL: KNI's `SkiaWebGlBackend` registers KNI's own WebGL2 context with SkiaSharp's Emscripten GL runtime
  (no second context exists to share objects with), so Skia and KNI drive one context. `KniStateInvalidation`
  resets KNI's state cache by reflection after each pass. None of that is reusable elsewhere.
- Vulkan: Stride has direct draw (`SkiaStrideVulkanContext.BeginDirectDraw`). Unity Vulkan has direct draw too.
- Metal: `MetalSkiaSurfaceFactory.CreateDirectSurface` in `Core.Metal` wraps the target's `MTLTexture` (an sRGB format through a Unorm view). The adapter supplies the texture and commits the engine's open command buffer first, which ends its render pass; Skia then loads the target and the engine's next pass keeps the contents. Used by Unity (`MetalUnityBackend.DrawDirect`).
- Godot: render target only is final on every renderer. Compatibility has no hook that runs while its target is bound. Forward+ and Mobile (RenderingDevice, Vulkan, D3D12 and Metal) defer all commands into a graph submitted at frame end, and nothing lets a native Skia queue submit run in order inside it.

## Multisampling

`SkiaRenderTarget2D`'s `multiSampleCount` (2, 4 or 8) works on the OpenGL backends only: MonoGame DesktopGL, Android and iOS, KNI DesktopGL, Android and iOS, and FNA OpenGL. Every other engine and backend throws `NotSupportedException` for a non-zero count, and 0 works everywhere. Portable code reads `SkiaRenderer.SupportsMultiSampledTargets`. Details and measurements are in the Multisampling section of [`docs/documentation/SkiaRenderTarget2D.md`](docs/documentation/SkiaRenderTarget2D.md#multisampling).

## Architecture

The library uses a backend abstraction (`SkiaBackend` base class) so each graphics API gets its
own implementation, documented per platform in `docs/`:

- [`docs/desktop/quickstart.md`](docs/desktop/quickstart.md) — MonoGame/KNI DesktopGL and
  WindowsDX (`SkiaGlBackend`, `SkiaAngleBackend`),
  project layout, and known limitations.
- [`docs/webgl/quickstart.md`](docs/webgl/quickstart.md) — KNI WebGL/Blazor (`SkiaWebGlBackend`).
- [`docs/android/quickstart.md`](docs/android/quickstart.md): KNI Android (`SkiaAndroidBackend`, `Core.OGL`'s `EglSharedContext`).
- [`docs/raylib/quickstart.md`](docs/raylib/quickstart.md) — raylib (`SkiaRenderTarget2D`),
  the first non-MonoGame engine and the first consumer of `Core.OGL` from outside the MonoGame
  family (tracked in [issue #3](https://github.com/vchelaru/SkiaGameRendering/issues/3); it does
  *not* derive from `SkiaBackend`/`SkiaTarget`, deliberately, since those are typed to MonoGame's
  `Texture2D`).

This was de-risked first as a throwaway spike (`spikes/raylib-ogl-v0/`, since removed — its
finding is preserved in issue #3's spike comment and carried into `Wgl.cs`'s doc comment).

## Known Issues / Cleanup

- **SetData workaround for lazy texture creation**: MonoGame WindowsDX's ANGLE backend forces D3D11
  resource allocation with a wasteful `SetData(new byte[w*h*4])` call (see `SkiaAngleBackend.md`).
  Need a cheaper trigger.
- **glFinish performance**: the ANGLE backends call `glFinish()` per renderable for GPU sync.
  Could potentially be relaxed to `glFlush()` if D3D11's internal synchronization is sufficient —
  unverified.

## Open Questions

- **KNI versions**: Which KNI package versions to target?

## Next Steps

1. ~~Run and archive the hardware benchmark matrix in `benchmarks/Benchmarks.WebGL/`~~ — done, closes [issue #5](https://github.com/vchelaru/SkiaGameRendering/issues/5). Follow-up: Firefox needed the Option A (shared-context) fallback; it shipped as `SkiaDirectCanvas` (see `docs/webgl/validated-baseline.md`).
2. Address the lazy-allocation issue above (ANGLE DLL packaging landed via issue #36).
3. Split per-graphics-API core libraries out of the per-engine adapters so a new engine (raylib) can reuse the GL/Skia interop instead of duplicating it — tracked in [issue #3](https://github.com/vchelaru/SkiaGameRendering/issues/3). The OGL split (`src/SkiaGameRendering.Core.OGL/`) landed via [#4](https://github.com/vchelaru/SkiaGameRendering/pull/4), and `src/SkiaGameRendering.Raylib.OGL/` now proves it against a real second (non-MonoGame) engine on both Windows and Linux (`GlxSharedContext`, now in `Core.OGL`, verified under WSLg — tracked in [issue #9](https://github.com/vchelaru/SkiaGameRendering/issues/9)). Issue #3 is not fully closed yet: step 3 (`SkiaAngleBackend` → `Core.ANGLE`, now also reused by `src/SkiaGameRendering.Kni.WindowsDX/`) has landed, leaving step 4 (`SkiaWebGlBackend` → `Core.WebGL`) as the only remaining unmigrated backend.
4. `src/SkiaGameRendering.Core.VK/` landed via [issue #23](https://github.com/vchelaru/SkiaGameRendering/issues/23) — engine-agnostic Vulkan/Skia interop, no host engine wired up yet. The Stride adapter (reflection into `Stride.Graphics.GraphicsDevice`/`Texture`'s Vulkan internals, plus queue-lock discipline) is the next step, tracked in [issue #54](https://github.com/vchelaru/SkiaGameRendering/issues/54).
5. `src/SkiaGameRendering.Core.D3D12/` landed — engine-agnostic D3D12/Skia interop (Skia's Ganesh D3D12 backend, per issue #24's now-accepted deprecation risk), same split `Core.VK` used. The MonoGame `WindowsDX12` backend (`SkiaDx12Backend`) was blocked on MonoGame#9536 and #9535 until they shipped in 3.8.6-preview.2; see `SkiaGameRendering-Notes.md` section 9.
6. `src/SkiaGameRendering.Godot/` landed — the first adapter needing no reflection at all (Godot exposes its device, queue and any RD texture's native handle through `RenderingDevice.GetDriverResource`), and the first with several graphics APIs behind one package, because Godot picks Vulkan, D3D12 or OpenGL at run time. It added `VkImageLayoutTransitioner` to `Core.VK` and `D3D12ResourceTransitioner` to `Core.D3D12` because Godot tracks every texture's layout/state itself and SkiaSharp cannot be asked which to leave a resource in (see `SkiaGameRendering-Notes.md` section 11). A third backend covers the Compatibility renderer over `Core.OGL` (a shared WGL/GLX context; that platform code moved from the raylib adapter into `Core.OGL` so both adapters use it). Follow-ups: publishing the NuGet package, EGL platforms for Compatibility (ANGLE, Android, Wayland), and Metal (since landed on `Core.Metal`). `tests/Tests.Godot` drives the real engine against the sample on each supported driver when `GODOT_BIN` is set and skips otherwise; CI downloads Godot and runs it on Windows (vulkan on lavapipe, d3d12 on WARP; opengl3 skipped, GDI will not load Mesa for it) and Linux (vulkan under synchronization validation, opengl3 on llvmpipe).
7. `src/SkiaGameRendering.Core.Metal/` landed — engine-agnostic Metal/Skia interop, the same "no host engine wired up yet" shape `Core.VK` and `Core.D3D12` started with. `tests/Tests.Core.Metal` runs on real Metal in the `metal-macos` CI job. The Godot Metal backend is its first consumer; Unity on macOS would be the next.

[Issue #2](https://github.com/vchelaru/SkiaGameRendering/issues/2) (closed) tracked the KNI upstream dependency: kniEngine/kni#2669 shipped in KNI v4.3.9001, so the KNI source patch/fork is gone (moved to stock NuGet). The one piece #2669 didn't cover — a public accessor for the current WebGL rendering context — is bridged with reflection (`WebGlCanvasUpload.cs`); pitching KNI a follow-up PR for it is tracked in [issue #13](https://github.com/vchelaru/SkiaGameRendering/issues/13). [Issue #14](https://github.com/vchelaru/SkiaGameRendering/issues/14) (closed) tracked an apparent 4.3.9001 startup crash that turned out not to be a KNI bug: `nkast.Wasm.*`'s dependency version bumped from 8.0.11 (under KNI 4.2.9001) to 10.0.3 (under 4.3.9001), and `wwwroot/index.html`'s hardcoded `<script>` version strings hadn't been updated to match — a silent 404, not a build failure. `eng/Versions.props`'s `NkastWasmCanvasVersion` must always match whatever the pinned `KniVersion` actually depends on.
