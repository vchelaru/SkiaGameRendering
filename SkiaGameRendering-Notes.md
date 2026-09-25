# SkiaSharp + MonoGame GPU Rendering: Exploration Notes

Captured from an exploration session reviewing [`mfigueirido/SkiaMonoGameRendering`](https://github.com/mfigueirido/SkiaMonoGameRendering) and thinking through how to extend it beyond its current OpenGL-only implementation.

---

## 1. What the existing library does (mechanism)

The library lets MonoGame (DesktopGL backend only) use SkiaSharp's GPU rendering to produce `Texture2D`s that MonoGame can then draw normally — with **no CPU readback**, i.e. zero-copy from Skia's output into MonoGame's texture.

The trick, as implemented in `SkiaGLUtils.cs` and `SkiaRenderer.cs`:

1. Grab MonoGame's SDL window handle and current GL context via reflection into MG internals (`Sdl.GL`, `MonoGame.OpenGL.GraphicsContext`, `GraphicsDevice.Context`).
2. Call `SDL_GL_SetAttribute(SDL_GL_SHARE_WITH_CURRENT_CONTEXT, 1)` and `SDL_GL_CreateContext` to make a **second** GL context that **shares objects** (textures, buffers, etc.) with MonoGame's main context. GL's object-sharing is the load-bearing feature.
3. Wrap that second context in a Skia `GRContext` via `GRContext.CreateGl()`.
4. Per frame (`SkiaRenderer.Draw`):
   - Allocate a `Texture2D` through MonoGame so MG owns it.
   - Read the raw GL texture ID via `glGetIntegerv(GL_TEXTURE_BINDING_2D)`.
   - Switch to the Skia GL context.
   - Build an FBO with that texture as `COLOR_ATTACHMENT0`, plus a depth/stencil renderbuffer.
   - Wrap the FBO in `GRBackendRenderTarget` → `SKSurface.Create`.
   - Hand the surface to user code (`ISkiaRenderable.DrawToSurface`), then `surface.Flush()`.
   - Switch back to MonoGame's GL context.

Why GL makes this easy: GL contexts natively support object sharing via a driver-level flag, SDL exposes it in one API call, and the driver handles synchronization implicitly.

### Code footprint
~600 lines total. Small, readable, well-structured. Any port would extend it rather than replace it.

### Key files
- `SkiaGLUtils.cs` — SDL/GL reflection glue + GL function loading + `SkiaGlManager` (context setup).
- `SkiaRenderer.cs` — per-frame FBO-over-MG-texture orchestration.
- `SkiaRenderableInfo.cs` — per-renderable cached state struct.
- `ISkiaRenderable.cs` — user-facing interface.

---

## 2. Feasibility across graphics APIs

The zero-copy texture-sharing pattern has two requirements:
- **(a)** Both sides (engine + Skia) must live on the same GPU device, OR there must be a supported cross-device shared-handle path.
- **(b)** SkiaSharp must expose a `GRContext` backend for that API.

### Summary table

| MonoGame backend | MG version | Skia backend path | Status |
|---|---|---|---|
| DesktopGL (OpenGL) | 3.8.4+ | Native GL (`GRContext.CreateGl`) | **Done** — this library |
| WindowsDX (D3D11) | 3.8.4 | ANGLE (GL ES → D3D11) | **Done** — see section 7 |
| Vulkan | 3.8.5 (preview) | Native Skia Vulkan (`GRContext.CreateVulkan`) *or* ANGLE-on-Vulkan | Most promising 3.8.5 target |
| D3D12 | 3.8.5 (native) | Native Skia Direct3D (`GRContext.CreateDirect3D`) | Skia-side interop built (`Core.D3D12`); MG glue blocked, see below |
| Metal | (not MG) | `GRContext.CreateMetal` | Not applicable |

D3D12 was originally decided against in [issue #24](https://github.com/vchelaru/SkiaGameRendering/issues/24) on Skia-side grounds alone: Ganesh (Skia's whole GPU-backend generation, D3D12 included) is a Google-maintained path Google has flagged for eventual replacement by Graphite. That risk is now knowingly accepted rather than waited out — new backends have been cheap enough to build that reaching the users already on D3D12-targeting engine runtimes (MonoGame 3.8.5's native `WindowsDX12`, Stride's D3D12 mode) is worth it. `src/SkiaGameRendering.Core.D3D12/` is the result: an engine-agnostic Skia/D3D12 interop layer, same split `Core.VK` used (issue #23) — built and tested (WARP-backed golden test) with no host engine wired up yet.

The MonoGame side is separately blocked, independent of the above: MG 3.8.5's new native `WindowsDX12`/`DesktopVK` platforms hide their device behind an opaque handle, so there is no `ID3D12Device`/`VkDevice` reachable via reflection today (see section 9 and issue #67). `MonoGame/MonoGame#9536` ("Exposing Native GPU Handles") adds the managed accessor that would fix this for both D3D12 and Vulkan, but it is still open with zero reviews as of this writing — the MonoGame `WindowsDX12` glue project waits on it merging with a locked API shape before it's safe to build against.

### Per-API detail

**OpenGL (current implementation).** See section 1.

**D3D11 via ANGLE.** ANGLE is Google's library that implements OpenGL ES on top of D3D11 (primarily), Vulkan, or Metal. Skia still runs its GL backend; ANGLE translates underneath. This is how SkiaSharp-on-UWP/WinUI works. Mechanism:
- Stand up ANGLE's EGL pointing at MG's existing `ID3D11Device`.
- Import MG-allocated `ID3D11Texture2D`s into the Skia GL context via `eglCreatePbufferFromClientBuffer` with `EGL_D3D_TEXTURE_ANGLE` / `EGL_ANGLE_d3d_share_handle_client_buffer`.
- Skia draws into them. Same zero-copy shape as the OpenGL version.

Template: [SkiaSharp.Views.WinUI](https://github.com/mono/SkiaSharp/tree/main/source/SkiaSharp.Views/SkiaSharp.Views.WinUI) + the [WinUI sample](https://github.com/mono/SkiaSharp/tree/main/samples/Basic/WinUI). Pay attention to the ANGLE SwapChains and the `Egl`/`Gles`/`GlesContext` bindings.

**Native Vulkan.** `GRContext.CreateVulkan` with `GRVkBackendContext` (`VkDevice`, `VkQueue`, `VkPhysicalDevice`, queue family index). Import MG-allocated `VkImage`s via `GRVkImageInfo` + `GRBackendTexture`. Same device as MG, so images are directly visible.

Vulkan-specific complexity:
- **Synchronization is explicit** — no implicit ordering like GL. Needs semaphores / pipeline barriers around Skia's submissions.
- **Image layout tracking** — Skia expects to know the layout on entry and leaves it in a known layout on exit. `GRVkImageInfo` carries this.
- Expect 200–500 lines of real work vs. the current ~15 lines of GL context plumbing.

**Native D3D12.** `GRContext.CreateDirect3D` with `GRD3DBackendContext` (`Adapter`, `Device`, `Queue`) — the same shared-device shape as native Vulkan, no ANGLE involved. Import a host-allocated `ID3D12Resource` via `GRD3DTextureResourceInfo` + `GRBackendTexture`/`GRBackendRenderTarget`. Verified directly against the pinned SkiaSharp assembly (reflection, not docs) when `Core.D3D12` was built: these types and the native `gr_direct_context_make_direct3d`/`gr_backendrendertarget_new_direct3d` entry points are real and callable.

D3D12-specific complexity, same shape as Vulkan's:
- **Synchronization is explicit** — `ID3D12CommandQueue::ExecuteCommandLists` needs the same external-lock discipline `Core.VK` documents for `vkQueueSubmit`.
- **Resource-state tracking** — the SkiaSharp version this repo pins has no way to read back the `D3D12_RESOURCE_STATES` a wrapped resource ends up in after a draw (no `gr_backendrendertarget_get_d3d_*` entry point, no `GrBackendSurfaceMutableState` binding), so a host needing certainty must insert its own `ResourceBarrier` rather than trust a reported value — see `D3D12SkiaSurfaceFactory.EndDraw`'s doc comment.

**Metal.** Not applicable to MonoGame.

---

## 3. The ANGLE vs. native-backend question (for MG 3.8.5)

This is the crux if you plan to support MG 3.8.5's new Vulkan backend.

### The case for ANGLE
- **Maintenance outsourcing.** Chrome on Windows runs on ANGLE. It is one of the most battle-tested graphics libraries in existence, continuously maintained by Google.
- **SkiaSharp's managed bindings are a weaker link** than Skia itself, though the Vulkan bindings are decent.
- GL→D3D11 translation through ANGLE is the specific path SkiaSharp-on-UWP uses, so there is existence-proof and a template to copy.
- Performance concern is mostly not real — ANGLE translates GLSL→HLSL at shader compile time, and per-dispatch overhead is small constants. Skia 2D workloads are fill-rate- and shader-compile-bound, not API-bound.

### The case against ANGLE (specifically for MG 3.8.5)
- **"Let Google maintain it" assumes Google maintains the specific feature you're relying on.** Chrome exercises the *forward* direction (GL → D3D for display). You need the *inverse* (import an engine-allocated native texture into Skia's GL context). Extensions like `EGL_D3D_TEXTURE_ANGLE`, `EGL_ANGLE_d3d_share_handle_client_buffer`, `EGL_ANGLE_vulkan_image` exist and work, but they're a secondary use case, not Chrome's primary dependency.
- **ANGLE's Vulkan backend is production** (ChromeOS, Android), but the Vulkan texture-import path is less exercised than the D3D11 one.
- When Skia has a native backend for your API, going through ANGLE is a translation layer for no reason — extra DLL, extra shader compile path, extra bug surface.

### Recommendation
| Target | Best path |
|---|---|
| MG 3.8.4 WindowsDX (D3D11) today | **ANGLE** — clear winner, UWP template exists |
| MG 3.8.5 Vulkan | Native Skia Vulkan (`GRContext.CreateVulkan`) is the cleanest 3.8.5 target — Skia's Vulkan backend is mature. |

Writing the D3D11/ANGLE version now doesn't lock you out of the 3.8.5 Vulkan future — the abstraction boundary (Skia draws into an MG-allocated texture, somehow) is the same.

---

## 4. Work plan if forking

### What's inherited from the existing repo
- SDL/GL reflection glue (`SkiaGLUtils.cs`)
- Per-frame FBO-over-MG-texture orchestration (`SkiaRenderer.cs`)
- `ISkiaRenderable` contract
- Context-switching + cleanup
- SkiaSharp color format → MonoGame `SurfaceFormat` mapping

The core design is sound. A port extends it, doesn't rewrite it.

### What's needed for a D3D11 / ANGLE port

A parallel `SkiaAngleManager` that:

1. **Extract MG's native D3D11 device** via reflection into MonoGame's WindowsDX backend (same pattern as current SDL reflection, different target).
2. **Initialize EGL against that device** using ANGLE's `EGL_PLATFORM_ANGLE_ANGLE` platform with `EGL_PLATFORM_ANGLE_TYPE_D3D11_ANGLE`.
3. **Create EGL display + context** that shares with MG's D3D11 device.
4. **Per-texture import**: for each MG-allocated `Texture2D`, use `eglCreatePbufferFromClientBuffer` with `EGL_D3D_TEXTURE_ANGLE` to wrap the underlying `ID3D11Texture2D` as a GL-visible surface.
5. `SkiaRenderer` becomes mostly backend-agnostic — it already thinks in GL terms, which is what ANGLE exposes.

Template: SkiaSharp.Views.WinUI source + the WinUI basic sample (links in section 2).

### Risks / experimental bits to watch
- **MG WindowsDX internal API shape.** Reflecting into SharpDX-wrapped internals can change between MG versions. Same fragility as current SDL reflection, different target. **This applies to the legacy D3D11 `WindowsDX` project on both 3.8.4 and 3.8.5 — see the 3.8.5 note below for why the new `WindowsDX12`/`DesktopVK` targets are a different, harder problem.**
- **`EGL_D3D_TEXTURE_ANGLE` with externally-allocated textures.** Works, documented, but less exercised than Chrome's main ANGLE usage. Expect to debug texture format / usage flag mismatches (render-target-capable, shader-resource, etc.).
- **Packaging.** Ship ANGLE DLLs (`libEGL.dll`, `libGLESv2.dll`) with your game. Not hard, just a packaging question.

### De-risking steps before committing to a fork
Concrete 30-minute verification pass:

1. Open MG 3.8.4 WindowsDX source; confirm `ID3D11Device` is reachable via reflection without absurd contortions.
2. Pull SkiaSharp WinUI basic sample; run it; confirm ANGLE-backed Skia rendering works on your machine.
3. Skim ANGLE's current `doc/DevSetup*.md` and feature-status docs; confirm D3D11 interop is still flagged stable.

If all three land green → bounded project. If any hits friction → you've learned something useful before investing.

---

## 5. Context thread with the original author

Issue: [mfigueirido/SkiaMonoGameRendering#2](https://github.com/mfigueirido/SkiaMonoGameRendering/issues/2)

Relevant points from the thread:
- Author confirmed a WindowsDX port would require replacing the OpenGL layer and said he'd "be happy to offer support if someone shows up and wants to deal with this." Explicit invitation to fork/contribute.
- Author believed (in 2022) SkiaSharp only supported OpenGL backends — this was true-ish then but is **outdated now**; modern SkiaSharp binds Vulkan `GRContext` creation.
- `@LilithSilver` identified the ANGLE path and the SkiaSharp WinUI sample as the template. The thread converged on ANGLE as the viable D3D route.
- Author confirmed platform support should match DesktopGL (Linux, Android probably work, untested).
- Consoles: native calls + SDK access issues make them hard regardless of API.

---

## 6. Terms / concepts worth knowing

- **ANGLE** — "Almost Native Graphics Layer Engine," Google's GL ES implementation on top of D3D11 / Vulkan / Metal. Used by Chrome, WebGL, SkiaSharp-on-UWP. Source: [google/angle](https://github.com/google/angle).
- **EGL** — the "windowing system" binding layer for OpenGL ES. ANGLE exposes a standard EGL interface. `EGL_PLATFORM_ANGLE_ANGLE` + platform-type attribute selects the backing API.
- **`GRContext`** — Skia's GPU context handle. One per graphics API: `CreateGl`, `CreateVulkan`, `CreateDirect3D`, `CreateMetal`.
- **`GRBackendRenderTarget` / `GRBackendTexture`** — Skia's way to wrap an externally-allocated GPU resource (FBO, VkImage, etc.) so Skia can draw into it without owning the allocation.
- **Object sharing (GL)** — GL contexts created with a shared-list flag see each other's texture/buffer object IDs. No equivalent in Vulkan — you share the *device* itself instead.
- **Shared NT handle (D3D)** — the cross-device/cross-API interop mechanism. `D3D11_RESOURCE_MISC_SHARED_NTHANDLE` on create, `OpenSharedHandle` on the other side. Needed if two different device objects must see the same texture.
- **Image layout (Vulkan)** — Vulkan images have an explicit layout state (e.g. `COLOR_ATTACHMENT_OPTIMAL`, `SHADER_READ_ONLY_OPTIMAL`) that must match what the current operation expects. Must be tracked and transitioned with barriers.

---

## 7. WindowsDX / ANGLE implementation (completed)

### What was built

A working `SkiaAngleBackend` for MonoGame 3.8.4 WindowsDX. Skia renders into MonoGame's D3D11 textures via ANGLE with zero-copy GPU sharing.

### Why ANGLE

MonoGame WindowsDX uses D3D11. SkiaSharp's GPU backend speaks OpenGL. ANGLE (Google's GL-to-D3D11 translator, the same library Chrome uses for WebGL on Windows) bridges the two: Skia issues GL calls, ANGLE translates them into D3D11 operations on the same device.

### The zero-copy trick

`eglCreateDeviceANGLE(EGL_D3D11_DEVICE_ANGLE, mgDevicePtr)` wraps MonoGame's existing D3D11 device as an ANGLE EGL device. Both ANGLE and MonoGame now share the same GPU device. MonoGame-allocated textures can be imported into ANGLE via `eglCreatePbufferFromClientBuffer(EGL_D3D_TEXTURE_ANGLE, texturePtr)`, creating an EGL surface backed by the D3D11 texture. Skia renders to that surface; MonoGame reads the result — no copies involved.

### The SwapDeviceContextState requirement

This was the hardest part. ANGLE modifies D3D11 state (shaders, blend modes, render targets, viewports, etc.) when it renders. MonoGame caches its own copy of D3D11 state internally and only re-applies when it detects a change. After ANGLE runs, MonoGame's cache is stale — it thinks the correct state is already set, so SpriteBatch silently draws nothing.

We tried several approaches (dirty flags, dummy state objects, ClearState) before finding that D3D11.1's `SwapDeviceContextState` is the correct solution. It atomically saves ALL context state before ANGLE and restores it after. This is the mechanism Microsoft designed for exactly this scenario (multiple rendering clients sharing one device).

### The RenderTarget2D requirement

ANGLE's `eglCreatePbufferFromClientBuffer` requires the D3D11 texture to have `D3D11_BIND_RENDER_TARGET`. MonoGame's `Texture2D` only creates textures with `D3D11_BIND_SHADER_RESOURCE`. `RenderTarget2D` (a Texture2D subclass) creates textures with both flags, which is what ANGLE needs.

### The lazy texture allocation workaround

MonoGame WindowsDX doesn't create the D3D11 GPU resource in the `Texture2D` constructor — it defers allocation until the texture is first used. The backend calls `SetData(new byte[...])` to force allocation so the native pointer can be extracted for ANGLE. This is wasteful and should be replaced with a cheaper trigger.

### ANGLE DLL resolution

ANGLE requires `libEGL.dll` and `libGLESv2.dll` at runtime, and `libGLESv2.dll` imports zlib's `z.dll`, which must sit next to it. The resolver in `AngleEgl.cs` looks for them in three places: app-local, NuGet runtimes folder, then Edge WebView's system copies at `C:\Windows\System32\Microsoft-Edge-WebView\`. Shipping your own ANGLE DLLs is recommended for production.

### Later: extraction into Core.ANGLE

Everything above (`AngleEgl`, the surface/texture-state factory, the `SwapDeviceContextState` fix) was later pulled out of `SkiaAngleBackend` into engine-agnostic `src/SkiaGameRendering.Core.ANGLE/` (`AngleEgl.cs`, `AngleSkiaSurfaceFactory.cs`), per [issue #3](https://github.com/vchelaru/SkiaGameRendering/issues/3)'s step 3. `SkiaAngleBackend` (MonoGame WindowsDX) is now a thin adapter over that factory, and `SkiaKniAngleBackend` (`src/SkiaGameRendering.Kni.WindowsDX/`) is a second adapter reusing it for KNI's WinForms DX11 platform — see TODO.md's Architecture section for the split.

---

## 8. WebGL / KNI (Blazor WebAssembly)

Full discussion is in a dedicated document: **`WebGL-KNI-Integration.md`** at the repo root. That doc covers:

- The problem (Gum on KNI with SpriteBatch interleaving and RenderTarget consumption).
- Why the desktop trick doesn't transfer to WebGL (spec-mandated no cross-context object sharing).
- The state-cache problem — the real obstacle when two libraries drive one context, and why it bit the WindowsDX/ANGLE port in section 7.
- The four options (A: shared WebGL context; B: CPU readback; C: two-canvas overlay; D: cross-context GPU blit via `texImage2D(canvas)`) with comparison table.
- Why Option D is the recommended path, plus the `OffscreenCanvas + transferToImageBitmap` fast-path variant.
- Option A reconsidered with a KNI-side `InvalidateStateCache()` patch reducing its implementation cost significantly.
- Spike v0 findings (spike since concluded and removed from the repo): initial results (Chrome/Edge ~0.25 ms, Firefox ~25 ms), four alternative upload paths identified to test whether any rescue Firefox.
- KNI-side changes worth making if forking KNI, and which of them are upstreamable to KNI vs better kept downstream.
- Where to pick up: the Firefox upload-path question is now tracked in [issue #5](https://github.com/vchelaru/SkiaGameRendering/issues/5) alongside the rest of the WebGL hardware-acceptance benchmark work; v1 (real KNI canvas) and v2 (full interleaving demo) followed and are done — see the integrated `Sample.Kni.WebGL`.

Short version of the recommendation: **build Option D**, which on Chrome/Edge measures ~0.25 ms upload at 1080p; Firefox unknown pending alternative-path measurement; fall back to Option A (with the KNI-fork state-cache patch) only if Firefox can't be rescued.

---

## 9. Open questions to revisit when MG 3.8.5 ships

- ~~Does MG 3.8.5 expose `VkDevice` / `VkQueue` publicly, or is reflection still required?~~ **Answered, and it's worse than "reflection required" — but only for the new native targets.** MG 3.8.5 ships two separate Windows platforms: the legacy `WindowsDX` (D3D11, `MonoGame.Framework.WindowsDX.csproj`, still SharpDX-based, unaffected by anything below) and the new `WindowsDX12` (D3D12, `MonoGame.Framework.Native.csproj`) backed by a single native C/C++ library (`native/`). Confirmed by reading `MonoGame.Framework/Platform/Native/GraphicsDevice.Native.cs` at tag `v3.8.5`: on `WindowsDX12`, `GraphicsDevice` holds `internal unsafe MGG_GraphicsDevice* Handle;` — an opaque pointer into that native library, not a `Vortice ID3D12Device` COM object. There is no managed device object left to reflect into on this target. Reaching a real `ID3D12Device` would mean going through MG's native interop layer (`MGG.*` P/Invoke surface) instead of C# reflection — a materially bigger undertaking than the reflection glue that ported the legacy WindowsDX in section 4. This blocker is specific to the new native `WindowsDX12` target; the legacy D3D11 `WindowsDX` project this library already ports (section 7) is a separate, untouched project on 3.8.5 same as 3.8.4. Filed as [issue #67](https://github.com/vchelaru/SkiaGameRendering/issues/67).
- **Vulkan on MG 3.8.5's `DesktopVK` platform has the identical blocker, confirmed.** The same native library builds the Vulkan backend (`native/monogame/vulkan/MGG_Vulkan.cpp`); its `MGG_GraphicsDevice` struct holds the real `VkDevice`/`VkQueue` (`vulkan/MGG_Vulkan.cpp:249-253`), but the public C API (`native/monogame/include/api_MGG.h`) exposes no getter for them — only opaque draw/state calls (`Draw`, `SetTexture`, `Clear`, etc.). There is no exported path back to a `VkDevice` any more than there is to `ID3D12Device`. Same fix scope as `WindowsDX12`: it needs new exports added to MG's native API, not a client-side workaround.

## 10. FNA / FNA3D (D3D11 and OpenGL, completed)

FNA's graphics layer is FNA3D, a native library with three drivers: SDL_GPU (first in FNA3D's
driver table, so the default wherever SDL3's GPU API initializes), D3D11 (Windows builds) and
OpenGL. `src/SkiaGameRendering.Fna.WindowsDX` is the D3D11 adapter, `src/SkiaGameRendering.Fna.OGL` the OpenGL one; `src/SkiaGameRendering.Fna/` holds the `FNA3D_GetSysRendererEXT` binding both link.

### Getting the device

FNA3D ships an opt-in extension header, `include/FNA3D_SysRenderer.h`, with two exports:
`FNA3D_GetSysRendererEXT` fills a struct with the driver's native handles (`ID3D11Device*` and the
immediate `ID3D11DeviceContext*` for D3D11, `SDL_GLContext` for OpenGL), and
`FNA3D_CreateSysTextureEXT` wraps an external texture as an `FNA3D_Texture*`. FNA's C# binding
doesn't declare either, so `Fna3dSysRenderer.cs` is a second `DllImport("FNA3D")` into the library
FNA already loaded. The `FNA3D_Device*` itself is `GraphicsDevice.GLDevice`, an internal field
reached by reflection (pinned in `tests/Tests.Fna.WindowsDX`). No FNA fork, no FNA3D fork.

### The SDL_GPU blocker

`SDLGPU_GetSysRenderer` in `FNA3D_Driver_SDL.c` is a `/* TODO */` memset, and
`SDLGPU_CreateSysTexture` returns NULL. It can't be finished from FNA3D alone: `SDL_GPUDevice` is
opaque and SDL3's only device properties are name/driver strings, no native handles. So the adapter
requires the D3D11 driver, selected with `FNA3D_FORCE_DRIVER=D3D11` before the window exists.
Verified in SDL3's `SDL_hints.c` that `SDL_GetHint` reads the environment variable of the same name
first, which is why a plain `Environment.SetEnvironmentVariable` in `Program.cs` is enough.
`SkiaFnaAngleBackend.Initialize` checks `rendererType` and throws with that instruction otherwise.
Same shape as MG 3.8.5's native backends (section 9): the fix is upstream exports, in SDL and then
FNA3D.

### Textures: the one native layout dependency

ANGLE needs `D3D11_BIND_RENDER_TARGET`, which FNA3D's D3D11 driver only sets for render targets, so
the adapter allocates `RenderTarget2D`s (same as MonoGame WindowsDX, section 7), and FNA creates the
GPU resource in the constructor, so the SetData workaround from section 7 isn't needed. What FNA3D
has no API for is reading the `ID3D11Resource*` back out of an `FNA3D_Texture*`. The
`CreateSysTextureEXT` route (make the texture ourselves, import it) was considered and rejected: a
sys texture has zeroed width/height/format inside FNA3D, so `Texture2D.GetData` on it fails at the
staging-texture step. Instead the adapter reads the first pointer of the `D3D11Texture` struct
(`FNA3D_Driver_D3D11.c`, "Cast FNA3D_Texture* to this!"), which is `handle`. That layout can't be
reflection-pinned; `CaptureTextureHandle` QueryInterfaces the pointer for `ID3D11Texture2D` before
ANGLE sees it, and the vendored `external/fnalibs/x64/FNA3D.dll` is the binary it was verified on.

### OpenGL adapter

Same design as MonoGame DesktopGL's `SkiaGlBackend`: `SDL_GL_CreateContext` with
`SDL_GL_SHARE_WITH_CURRENT_CONTEXT` gives Skia its own context sharing FNA's texture namespace, and
FNA3D's aggressive GL state cache never sees Skia's state changes. The SDL calls go through the
SDL3-CS binding FNA compiles into `FNA.dll` (`SDL3.SDL` is public there), so nothing is reflected
on the SDL side; SDL2 FNA builds (`FNA_PLATFORM_BACKEND=SDL2`) aren't supported. FNA's context and
window come from `SDL_GL_GetCurrentContext/Window` on the game thread, cross-checked against the
`opengl.context` FNA3D reports. The GL texture name is the first field of FNA3D's `OpenGLTexture`
struct (same no-API situation as D3D11), checked with `glIsTexture` on the shared context. Plain
`Texture2D`s work here (FNA3D's GL driver allocates storage in the constructor); `TopLeft` origin,
same as MonoGame, and the golden comes out identical to the DesktopGL ones.

### Build and test plumbing

- FNA is not on NuGet. `external/FNA` is a submodule pinned to release tag 26.09 with only the
  five C#-binding submodules it needs initialized (`lib/SDL2-CS`, `lib/SDL3-CS`, `lib/FAudio`,
  `lib/Theorafile`, `lib/dav1dfile`; not `lib/FNA3D`, which is the native source tree). The
  package references `FNA.Core.csproj` with `PrivateAssets="All"`, the Gum.FNA arrangement.
- fnalibs are only published as expiring GitHub Actions artifacts of `FNA-XNA/fnalibs-dailies`
  (authenticated download), so the Windows x64 set is checked in under `external/fnalibs/` and
  `FnaLibs.props` copies it next to the sample and test binaries.
- `tests/Tests.Fna.WindowsDX` runs a hidden one-frame FNA `Game` (FNA has no headless device
  path: `FNA3D_PrepareWindowAttributes` only runs inside FNA's window creation) on WARP via the
  `FNA3D_D3D11_USE_WARP=1` hint. Its golden came out byte-identical to the MonoGame WindowsDX one,
  which is what you'd expect from the same ANGLE build on the same rasterizer.
- `tests/Tests.Fna.OGL` is the same one-frame Game on `FNA3D_FORCE_DRIVER=OpenGL`, Mesa-gated like
  the DesktopGL goldens (`MesaVendor.props`, `SKIAGAMERENDERING_PINNED_RASTERIZER`). FNA loads GL
  through SDL after `VendoredOpenGl.PreloadIfPresent`, so the vendored `opengl32.dll` takes effect
  for it the same way it does for MonoGame. With `tests/mesa-vendor/` present and
  `GALLIUM_DRIVER` unset, Mesa's D3D12 path crashes the test host (MonoGame's DesktopGL tests
  too); that's the known landmine in the `headless-gpu-testing` skill, not the adapter.

## 11. Godot 4 (Vulkan, D3D12 and Compatibility/OpenGL, completed)

`src/SkiaGameRendering.Godot` targets Godot 4.7+ .NET projects on the Forward+/Mobile renderer
with the Vulkan or D3D12 driver and on the Compatibility renderer with the native OpenGL driver -
one package, backend chosen at run time from `RenderingServer.GetCurrentRenderingDriverName()`,
because Godot decides its renderer and driver per run (project settings, `--rendering-driver`/
`--rendering-method`, or an automatic fallback). It is the first adapter in this
repo that reaches the engine's device with **no reflection**: `RenderingDevice.GetDriverResource(DriverResource.X, rid, 0)` is public API
(Godot 4.3+) returning the raw `VkInstance` (`TopmostObject`), `VkPhysicalDevice`, `VkDevice`
(`LogicalDevice`), `VkQueue` (`CommandQueue`), queue family index (`QueueFamily`), and for any RD
texture its `VkImage` (`Texture`) and `VkFormat` (`TextureDataFormat`). The texture Skia draws into
is created with `RenderingDevice.TextureCreate` and shown through the stock `Texture2DRD` resource,
which builds a shared *view* of the RD texture (no copy). Zero-copy, same as every other backend.

### What made it non-trivial: Godot tracks image layouts

Godot's `RenderingDeviceGraph` (servers/rendering/rendering_device_graph.cpp) derives each mutable
texture's `VkImageLayout` from the last `ResourceUsage` it recorded, and only emits a barrier when
that usage changes. A texture Godot merely samples starts at `RESOURCE_USAGE_NONE` (`UNDEFINED`),
gets one `UNDEFINED -> SHADER_READ_ONLY_OPTIMAL` barrier the first frame it is drawn, and then
**no barrier ever again** - every later frame's descriptor says `SHADER_READ_ONLY_OPTIMAL` and
assumes it. Skia leaves a wrapped render target in `COLOR_ATTACHMENT_OPTIMAL`, and SkiaSharp
3.119.4 has no `GrBackendSurfaceMutableState` binding to request otherwise (the same gap
`VkSkiaSurfaceFactory.EndDraw` documents for Stride). Left alone, Godot samples an image whose real
layout never matches its bookkeeping - which happens to work on NVIDIA and is undefined elsewhere.

The fix is the "host inserts its own barrier" fallback Core.VK already prescribed, now packaged as
`Core.VK`'s `VkImageLayoutTransitioner`: after Skia's flush, `End()` submits a
`COLOR_ATTACHMENT_OPTIMAL -> SHADER_READ_ONLY_OPTIMAL` barrier on Godot's queue, and `Begin()`
re-wraps the `GRBackendRenderTarget`/`SKSurface` each frame with that layout as Skia's starting
point (Skia caches the last layout it set, so a persistent surface would skip its own transition
back to `COLOR_ATTACHMENT_OPTIMAL`). Two more details close the remaining holes. Godot's single
barrier for the texture has `oldLayout = UNDEFINED`, which the spec allows to discard contents and
tiled/mobile drivers do; so the constructor runs a one-triangle fragment-shader pass that samples
the texture and then flushes and stalls the graph (`texture_get_data` on a 1x1 scratch texture is
Godot's public "execute everything recorded so far and wait"), making that transition happen
before Skia ever draws and leaving Godot's tracker on TEXTURE_SAMPLE for good. Fragment rather than
compute matters on D3D12's legacy path, where it narrows the state to `PIXEL_SHADER_RESOURCE`, the
state the 2D canvas samples in later. And `Begin()`
records a 1x1 modulate-by-white draw so a frame with no other Skia work still executes a render
pass and really ends in `COLOR_ATTACHMENT_OPTIMAL` - otherwise the hand-back barrier's
`oldLayout` would be wrong for a `Begin(clear: false)`/`End()` early-out. Verified clean under
Godot's `--gpu-validation` (Khronos validation layer 1.3.261.1) across the sample and a scenario
suite covering no-draw frames, dispose/recreate, multiple targets and the Separate thread model.

A second validation finding: `Texture2DRD` creates an sRGB view of the image, which is only legal on
an image created with `VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT` (VUID-VkImageViewCreateInfo-image-01762).
Godot sets that bit when `RDTextureFormat` lists shareable formats, so the adapter declares the
UNORM and sRGB twins.

### Threading and synchronization

`get_driver_resource` and every RD command are guarded by `ERR_RENDER_THREAD_GUARD`. Under the
default `rendering/driver/threads/thread_model` ("Safe") the render thread is the main thread and
Godot's frame submit happens in `RS::draw`, after `_Process`, so Skia submits from `_Process` are
serialized with it. Godot's own per-queue `submit_mutex` is unreachable from C#, and it guards more
than the frame submit: texture/buffer uploads (`_acquire_transfer_worker`) can submit from any
thread. Godot creates one queue per family and puts uploads on the family with the fewest flags
that include `TRANSFER`, so on GPUs with a dedicated transfer family they never touch Skia's queue.
Without one (integrated, mobile, MoltenVK) they share it, and an upload from a loader thread can
race Skia's submit. `GodotVulkanQueueFamilies.UploadsShareMainQueue` mirrors Godot's pick and
`Initialize` pushes a Godot warning in that case; D3D12 queues are free-threaded, so only Vulkan is
affected. Under "Separate" (experimental in Godot, verified with
`--render-thread separate`), construction, `Begin`/`End` and `Initialize` must go through
`RenderingServer.CallOnRenderThread`; every entry point checks `RenderingServer.IsOnRenderThread()`
and throws with that instruction. `Dispose` is the one call that has to straddle both threads there:
clearing `Texture2DRD.texture_rd_rid` emits `changed`, and a `Sprite2D` showing the texture answers
with `queue_redraw`, which Godot only allows from the node's thread (the main thread), while freeing
the RD texture must happen on the render thread. So `SkiaGodotRenderTarget2D.Dispose` runs the half
that belongs to the calling thread immediately and hands the other half over (`CallOnRenderThread`
or `CallDeferred`), and the Texture2DRD wrapper itself is never `Dispose()`d - it is a refcounted
resource other nodes may still hold.

Nothing per frame waits on the GPU. `End()` submits Skia's work asynchronously
(`VkSkiaSurfaceFactory.EndDraw(synchronous: false)`; Stride keeps the synchronous default), because Godot's own sampling of the texture is its frame submit on the same queue, later
in the frame, and queue order alone makes it see the finished draw. The hand-back barrier goes
through a ring of command buffers in `VkImageLayoutTransitioner` that grows (to 64) instead of
waiting when the oldest is still in flight; the only deliberate stall is in `Dispose`, before Godot frees
the `VkImage`. A note on Godot's Texture2DRD: assigning an invalid RID frees its RenderingServer view
and zeroes its size but leaves `texture_rd_rid` reporting the old value (`texture_rd.cpp`), so size
is the observable signal that a texture was detached.

### Other findings

- Godot creates its `VkInstance` against `VK_API_VERSION_1_2` (1.0 on a 1.0 loader) and enables
  every core `VkPhysicalDeviceFeatures` member it finds supported, so Skia is told 1.2 (clamped
  from `VkSkiaSurfaceFactory.QueryApiVersion`) and left to query features itself.
- Godot 4.6+ defaults **new** Windows projects to the `d3d12` driver (supported, see below) and
  macOS to `metal`. macOS is unsupported on every driver: no Metal interop here, and SkiaSharp
  3.119.4's macOS native library has no Vulkan backend (`GRContext.CreateVulkan` returns null on
  MoltenVK; the dylib has none of the `vk*` entry-point names the Windows build carries).
  `SkiaGodotRenderer.Initialize` throws on macOS.
- Godot's default 2D pipeline is gamma-space (`hdr_2d` off), so a plain UNORM texture holding
  Skia's sRGB bytes displays 1:1 - none of the Stride adapter's linear-space compensation. The
  sample's pure red and CornflowerBlue read back exactly.
- A C# class library for Godot is a plain `Microsoft.NET.Sdk` project referencing the `GodotSharp`
  package; only the game project uses `Godot.NET.Sdk`, and only its assembly is scanned for script
  classes. The adapter's public types are ordinary classes handing back an engine-owned
  `Texture2DRD`, so no source generators are involved.
- `--headless` uses the dummy renderer (`GetRenderingDevice()` returns null), so the only way to
  test this adapter is a real Godot window. `tests/Tests.Godot.VK` launches the engine against the
  sample with a `--screenshot` user argument and probes the PNG; it runs only when `GODOT_BIN`
  points at a Godot .NET executable and skips otherwise. CI sets it on Windows and Linux (`master.yml`).
- Beyond the sample, the adapter was exercised as a real NuGet package (packed, restored from a
  local feed into a fresh `Godot.NET.Sdk` project) through a scenario suite: several targets at
  once on `Sprite2D`, `TextureRect` and `_Draw()`/`DrawTexture`; a BGRA target; transparent clears
  with the premultiplied material (half-alpha pixels blend to the expected value); dispose and
  recreate mid-run; a draw-once texture still intact 60 frames later; every misuse path (Begin
  twice, End before Begin, Dispose mid-frame, use after Dispose, off-thread calls, double Initialize);
  200 create/draw/dispose cycles with no RID leak; 600 frames with three targets (flat managed and
  RD memory); the Separate thread model end to end; and the D3D12 / Compatibility error messages.
  All of it clean under `--gpu-validation`. That suite lives outside the repo (it needs a Godot
  binary); the pieces worth keeping are the sample, the `GODOT_BIN` test, and this list.
- Prior art: no GPU-path Skia integration for Godot existed. The published Godot+SkiaSharp projects
  are CPU readback (`SKBitmap` -> `ImageTexture`), and other external renderers embedded in Godot
  (Servo, Rive) share *memory* (external-memory handles) rather than the logical device.

### D3D12

Godot 4.6+ writes `rendering/rendering_device/driver.windows="d3d12"` into every new Windows
project, so D3D12 is the path most new Godot Windows projects will hit. The same
`GetDriverResource` calls return the `IDXGIAdapter1` (`PhysicalDevice`), `ID3D12Device`
(`LogicalDevice`), `ID3D12CommandQueue` (`CommandQueue`) and a texture's `ID3D12Resource`
(`Texture`), which is everything `Core.D3D12`'s `D3D12SkiaSurfaceFactory` needs. Two things made it
different from Vulkan:

- **Godot's D3D12 textures are typeless, and Skia cannot render into a typeless resource.**
  `texture_create` in `rendering_device_driver_d3d12.cpp` always uses the format's typeless
  family (`RD_TO_D3D12_FORMAT[...].family`) so UNORM and sRGB views can share one resource, and
  `TextureDataFormat` reports that typeless format. Skia's D3D12 backend creates its render-target
  view with a null descriptor (`GrD3DCpuDescriptorManager::createRenderTargetView`), which D3D12
  rejects for typeless resources. `TextureCreateFromExtension` (Godot wrapping a typed resource we
  own) is a dead end on this driver too: `Texture2DRD` always goes through `texture_create_shared`,
  which the D3D12 driver refuses for a texture without a Godot-owned allocation. So the D3D12
  backend renders into a typed `ID3D12Resource` this library allocates
  (`D3D12SkiaSurfaceFactory.CreateRenderTargetResource`, raw COM) and `End()` queues one
  `CopyResource` into Godot's texture - typed UNORM into TYPELESS of the same family is a legal
  copy. GPU-to-GPU, no CPU readback, but not zero-copy; `SkiaGodotRenderer.IsZeroCopy` reports it.
  A side benefit: Skia owns its surface outright, so nothing is re-wrapped per frame and no
  sentinel draw is needed - the copy returns Skia's resource to `RENDER_TARGET`, which is what
  Skia believes.
- **Godot's D3D12 driver tracks state two different ways.** With
  `D3D12_FEATURE_D3D12_OPTIONS12.EnhancedBarriersSupported` it uses the render graph's usage
  tracking like Vulkan and maps a sampled texture to `D3D12_BARRIER_LAYOUT_SHADER_RESOURCE`,
  whose legacy-state equivalent is `ALL_SHADER_RESOURCE`. Without it, it keeps legacy
  per-subresource states and `command_uniform_set_prepare_for_use` narrows a texture sampled only
  by a fragment shader to `PIXEL_SHADER_RESOURCE`; its later "is this transition redundant" check
  (`_resource_transition_batch`) passes whenever the current state already has every wanted bit,
  so once the texture sits in the state canvas rendering wants, Godot never barriers it again.
  The backend queries `OPTIONS12` the same way Godot does and hands the texture back in the
  matching state after every copy. This is also why priming is a *fragment-shader* draw rather
  than a compute dispatch on both backends: compute would leave the legacy path in
  `NON_PIXEL_SHADER_RESOURCE`, which Godot's first canvas draw would then transition away from -
  a state change this library cannot observe. The remaining caveat: sampling the texture from a
  vertex or compute shader on the legacy path moves Godot's belief, and the hand-back state no
  longer matches (the debug layer reports it; hardware mostly tolerates it). 2D canvas use is
  fragment-only. The dev box that verified all this runs Godot's D3D12 with enhanced barriers
  (`SkiaGodotRenderer.D3D12UsesEnhancedBarriers` reports it); the legacy path is implemented from
  the driver source but not yet exercised on hardware.

`Core.D3D12` gained `D3D12ResourceTransitioner` (a ring of command allocator/list pairs and a
fence, `Transition` and `CopyWithTransitions`), the `CreateRenderTargetResource`/`ReleaseResource`
helpers, `QueryEnhancedBarriersSupported`, and `EndDraw(synchronous)`, all over raw COM vtables in
`D3D12Com.cs` (slots cross-checked against `tests/Tests.Core.D3D12/D3D12TestNative.cs`).

### Compatibility renderer (OpenGL)

The Compatibility renderer has no `RenderingDevice` (`GetRenderingDevice()` is null), so none of
the above applies; what Godot does expose is the window (`DisplayServer.WindowGetNativeHandle`)
and, for any `ImageTexture`, its `GLuint` (`RenderingServer.TextureGetNativeHandle`). That is
exactly the raylib adapter's situation, and the backend is that adapter's shape: `Core.OGL`'s
`WglSharedContext`/`GlxSharedContext` (shared with the raylib adapter) create a second context sharing Godot's object
namespace off the context current on the render thread, Skia gets its own `GRContext` on it, and
`Core.OGL`'s `GlSkiaSurfaceFactory` wraps the texture in an FBO with `GRSurfaceOrigin.TopLeft`:
Godot uploads image row 0 to texel row 0 and samples v=0 as the top, so Skia must write canvas row 0
into texel row 0 - the opposite of the raylib adapter's `BottomLeft`, which renders everything
upside down here. A symmetric test image such as the sample circle cannot show the difference. Separate contexts keep
Godot's cached GL state and Skia's apart; cross-context visibility is GL's shared-object rule
(Skia's flush ends in `glFlush`, Godot's canvas binds the texture per draw). Zero-copy, a
persistent surface, no priming or hand-back. Limits: Godot's `Image.Format` has no BGRA/10-bit
formats, so RGBA8 only; and only the native `opengl3` driver on Windows (WGL) and Linux X11 (GLX) -
`opengl3_angle`, `opengl3_es`, Wayland and macOS are EGL/NSOpenGL contexts with no platform code
here yet, and web exports cannot P/Invoke GL at all. Verified on Windows; the GLX path compiles
from the raylib adapter's code but has not been run under Godot.

