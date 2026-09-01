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
| D3D12 | 3.8.5 (preview) | Native Skia D3D12 (`GRContext.CreateDirect3D`) *or* ANGLE-on-D3D12 | Both partially unready — see below |
| Vulkan | 3.8.5 (preview) | Native Skia Vulkan (`GRContext.CreateVulkan`) *or* ANGLE-on-Vulkan | Most promising 3.8.5 target |
| Metal | (not MG) | `GRContext.CreateMetal` | Not applicable |

### Per-API detail

**OpenGL (current implementation).** See section 1.

**D3D11 via ANGLE.** ANGLE is Google's library that implements OpenGL ES on top of D3D11 (primarily), Vulkan, or Metal. Skia still runs its GL backend; ANGLE translates underneath. This is how SkiaSharp-on-UWP/WinUI works. Mechanism:
- Stand up ANGLE's EGL pointing at MG's existing `ID3D11Device`.
- Import MG-allocated `ID3D11Texture2D`s into the Skia GL context via `eglCreatePbufferFromClientBuffer` with `EGL_D3D_TEXTURE_ANGLE` / `EGL_ANGLE_d3d_share_handle_client_buffer`.
- Skia draws into them. Same zero-copy shape as the OpenGL version.

Template: [SkiaSharp.Views.WinUI](https://github.com/mono/SkiaSharp/tree/main/source/SkiaSharp.Views/SkiaSharp.Views.WinUI) + the [WinUI sample](https://github.com/mono/SkiaSharp/tree/main/samples/Basic/WinUI). Pay attention to the ANGLE SwapChains and the `Egl`/`Gles`/`GlesContext` bindings.

**Native D3D12.** `GRContext.CreateDirect3D` with `GRD3DBackendContext` (device + command queue + adapter). Same device as MG, so `ID3D12Resource` is directly shareable — no NT-handle interop needed. Wrap resources in `GRD3DTextureResourceInfo` (resource pointer, current state, DXGI format). Synchronization via D3D12 fences; must track and restore resource state around Skia submissions.

Caveat: **SkiaSharp's managed D3D12 bindings have historically been the least-polished of Skia's backends.** Verify `GRContext.CreateDirect3D` is actually exported in the SkiaSharp version you'd use at port time.

**Native Vulkan.** `GRContext.CreateVulkan` with `GRVkBackendContext` (`VkDevice`, `VkQueue`, `VkPhysicalDevice`, queue family index). Import MG-allocated `VkImage`s via `GRVkImageInfo` + `GRBackendTexture`. Same device as MG, so images are directly visible.

Vulkan-specific complexity:
- **Synchronization is explicit** — no implicit ordering like GL. Needs semaphores / pipeline barriers around Skia's submissions.
- **Image layout tracking** — Skia expects to know the layout on entry and leaves it in a known layout on exit. `GRVkImageInfo` carries this.
- Expect 200–500 lines of real work vs. the current ~15 lines of GL context plumbing.

**Metal.** Not applicable to MonoGame.

---

## 3. The ANGLE vs. native-backend question (for MG 3.8.5)

This is the crux if you plan to support MG 3.8.5's new D3D12 / Vulkan backends.

### The case for ANGLE
- **Maintenance outsourcing.** Chrome on Windows runs on ANGLE. It is one of the most battle-tested graphics libraries in existence, continuously maintained by Google.
- **SkiaSharp's managed bindings are a weaker link** than Skia itself. Vulkan bindings are decent; D3D12 bindings have lagged.
- GL→D3D11 translation through ANGLE is the specific path SkiaSharp-on-UWP uses, so there is existence-proof and a template to copy.
- Performance concern is mostly not real — ANGLE translates GLSL→HLSL at shader compile time, and per-dispatch overhead is small constants. Skia 2D workloads are fill-rate- and shader-compile-bound, not API-bound.

### The case against ANGLE (specifically for MG 3.8.5)
- **"Let Google maintain it" assumes Google maintains the specific feature you're relying on.** Chrome exercises the *forward* direction (GL → D3D for display). You need the *inverse* (import an engine-allocated native texture into Skia's GL context). Extensions like `EGL_D3D_TEXTURE_ANGLE`, `EGL_ANGLE_d3d_share_handle_client_buffer`, `EGL_ANGLE_vulkan_image` exist and work, but they're a secondary use case, not Chrome's primary dependency.
- **ANGLE's D3D12 backend is not production-quality.** Chrome still defaults to D3D11 on Windows. Using ANGLE with an MG D3D12 build would mean ANGLE stands up its own D3D11 device on the side → cross-device shared-handle interop → the ugly path.
- **ANGLE's Vulkan backend is production** (ChromeOS, Android), but the Vulkan texture-import path is less exercised than the D3D11 one.
- When Skia has a native backend for your API, going through ANGLE is a translation layer for no reason — extra DLL, extra shader compile path, extra bug surface.

### Recommendation
| Target | Best path |
|---|---|
| MG 3.8.4 WindowsDX (D3D11) today | **ANGLE** — clear winner, UWP template exists |
| MG 3.8.5 D3D12 | Decide at port time. Check (a) is SkiaSharp shipping `GRContext.CreateDirect3D`? (b) has ANGLE's D3D12 backend moved off experimental? Whichever is more mature wins. |
| MG 3.8.5 Vulkan | Native Skia Vulkan (`GRContext.CreateVulkan`) is the cleanest target of the three — Skia's Vulkan backend is mature. |

Writing the D3D11/ANGLE version now doesn't lock you out of either 3.8.5 future — the abstraction boundary (Skia draws into an MG-allocated texture, somehow) is the same.

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
- **MG WindowsDX internal API shape.** Reflecting into SharpDX-wrapped internals can change between MG versions. Same fragility as current SDL reflection, different target.
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
- Author believed (in 2022) SkiaSharp only supported OpenGL backends — this was true-ish then but is **outdated now**; modern SkiaSharp binds Vulkan and D3D12 `GRContext` creation.
- `@LilithSilver` identified the ANGLE path and the SkiaSharp WinUI sample as the template. The thread converged on ANGLE as the viable D3D route.
- Author confirmed platform support should match DesktopGL (Linux, Android probably work, untested).
- Consoles: native calls + SDK access issues make them hard regardless of API.

---

## 6. Terms / concepts worth knowing

- **ANGLE** — "Almost Native Graphics Layer Engine," Google's GL ES implementation on top of D3D11 / Vulkan / Metal. Used by Chrome, WebGL, SkiaSharp-on-UWP. Source: [google/angle](https://github.com/google/angle).
- **EGL** — the "windowing system" binding layer for OpenGL ES. ANGLE exposes a standard EGL interface. `EGL_PLATFORM_ANGLE_ANGLE` + platform-type attribute selects the backing API.
- **`GRContext`** — Skia's GPU context handle. One per graphics API: `CreateGl`, `CreateVulkan`, `CreateDirect3D`, `CreateMetal`.
- **`GRBackendRenderTarget` / `GRBackendTexture`** — Skia's way to wrap an externally-allocated GPU resource (FBO, VkImage, D3D12Resource, etc.) so Skia can draw into it without owning the allocation.
- **Object sharing (GL)** — GL contexts created with a shared-list flag see each other's texture/buffer object IDs. No equivalent in Vulkan/D3D12 — you share the *device* itself instead.
- **Shared NT handle (D3D)** — the cross-device/cross-API interop mechanism. `D3D11_RESOURCE_MISC_SHARED_NTHANDLE` on create, `OpenSharedHandle` on the other side. Needed if two different device objects must see the same texture.
- **Image layout (Vulkan)** — Vulkan images have an explicit layout state (e.g. `COLOR_ATTACHMENT_OPTIMAL`, `SHADER_READ_ONLY_OPTIMAL`) that must match what the current operation expects. Must be tracked and transitioned with barriers.
- **Resource state (D3D12)** — D3D12's equivalent of Vulkan image layout; transitioned via resource barriers.

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

ANGLE requires `libEGL.dll` and `libGLESv2.dll` at runtime. The resolver in `AngleEgl.cs` looks for them in three places: app-local, NuGet runtimes folder, then Edge WebView's system copies at `C:\Windows\System32\Microsoft-Edge-WebView\`. Shipping your own ANGLE DLLs is recommended for production.

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

- Is `GRContext.CreateDirect3D` exposed and working in the SkiaSharp version available at that time?
- Has ANGLE's D3D12 backend moved off experimental status?
- Does MG 3.8.5 expose `VkDevice` / `VkQueue` / `ID3D12Device` / `ID3D12CommandQueue` publicly, or is reflection still required?
- Does MG 3.8.5's texture allocation respect usage flags needed for Skia interop (render-target, storage, appropriate format support)?
