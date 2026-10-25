# Architecture

The library uses a backend abstraction (`SkiaBackend` base class) so each graphics API gets its own implementation. Core source files are shared across platform-specific library projects via linked includes:

- `src/SkiaGameRendering/` — DesktopGL library (core + `SkiaGlBackend`)
- `src/SkiaGameRendering.Core.OGL/` — engine-agnostic raw-GL/Skia FBO interop shared by GL-based backends
- `src/SkiaGameRendering.Core.ANGLE/` — engine-agnostic D3D11/ANGLE interop shared by ANGLE-based backends
- `src/SkiaGameRendering.WindowsDX/` — MonoGame WindowsDX library (shared core + `SkiaAngleBackend`, on `Core.ANGLE`)
- `src/SkiaGameRendering.Kni.DesktopGL/` — KNI DesktopGL library (shared core + `SkiaGlBackend`)
- `src/SkiaGameRendering.Kni.WindowsDX/` — KNI WindowsDX library (shared core + `SkiaAngleBackend`, on `Core.ANGLE`)
- `src/SkiaGameRendering.Kni.Android/`: KNI Android library (shared core + `SkiaAndroidBackend`, on `Core.OGL`'s `EglSharedContext`; targets `net10.0-android36.0`)
- `src/SkiaGameRendering.iOS/`: MonoGame iOS library (shared core + `SkiaIosBackend` and its `EaglSharedContext`, on `Core.OGL`; targets `net10.0-ios26.5`)
- `src/SkiaGameRendering.Kni.iOS/`: KNI iOS library (links `SkiaIosBackend` and `EaglSharedContext` from the MonoGame iOS project, plus KNI's texture and render-target handling; targets `net10.0-ios26.5`)
- `src/SkiaGameRendering.Kni.WebGL/` — KNI/Blazor library (shared core + `SkiaWebGlBackend`)
- `src/SkiaGameRendering.Fna.WindowsDX/`: FNA library (shared core + `SkiaAngleBackend`, on `Core.ANGLE`, Windows/D3D11 only)
- `src/SkiaGameRendering.Fna.WebGL/`: FNA in the browser (shared core + `SkiaFnaWebGlBackend`). FNA3D and Skia drive one WebGL context, with the GL state FNA3D caches saved and restored around each Skia pass in JS
- `src/SkiaGameRendering.Fna.OGL/`: FNA library (shared core + `SkiaGlBackend`, on `Core.OGL`; `src/SkiaGameRendering.Fna/` holds the FNA3D binding both FNA packages link)
- `src/SkiaGameRendering.Raylib.OGL/` — raylib library (shared `Core.OGL` + raylib's `SkiaRenderTarget2D`, `SkiaDirectCanvas`, `SkiaRenderer`)
- `src/SkiaGameRendering.Stride.D3D11/` — Stride library (shared `Core.ANGLE` + `SkiaRenderTarget2D`, Windows/D3D11 only)
- `src/SkiaGameRendering.Core.VK/` — engine-agnostic Vulkan/Skia interop shared by Vulkan-based backends
- `src/SkiaGameRendering.Core.Metal/`: engine-agnostic Metal/Skia interop shared by Metal-based backends (Godot on macOS today)
- `src/SkiaGameRendering.Stride.VK/` — Stride library (shared `Core.VK` + `SkiaRenderTarget2D`, Windows/Linux/macOS)
- `src/SkiaGameRendering.Core.D3D12/`: engine-agnostic D3D12/Skia interop shared by D3D12-based backends
- `src/SkiaGameRendering.Stride.D3D12/`: Stride library (shared `Core.D3D12` + `SkiaRenderTarget2D`, Windows/D3D12 only)
- `unity/com.vchelaru.skiagamerendering/`: Unity UPM package (`Core.ANGLE`'s, `Core.D3D12`'s, `Core.VK`'s and `Core.Metal`'s netstandard2.1 builds + `SkiaRenderTarget2D`, Windows/D3D11, Windows/D3D12, Windows/Vulkan, macOS/Metal and Android/OpenGL ES; the plugins' sources are `unity/native/SkiaUnityD3D12/`, `unity/native/SkiaUnityVulkan/` and `unity/native/SkiaUnityMetal/`)
- `src/SkiaGameRendering.Godot/` — Godot library (`Core.VK`, `Core.D3D12`, `Core.Metal` and `Core.OGL` behind one `SkiaRenderTarget2D`, backend chosen from the running driver; no reflection, all public Godot API)

## How each backend works

| Backend | Technique |
|---------|-----------|
| OpenGL (MonoGame/KNI DesktopGL, FNA OpenGL) | Second SDL GL context shared with the engine's |
| OpenGL ES (Unity Android) | The same `EglSharedContext` as KNI Android, created on Unity's render thread from its current EGL context; Unity's GL ES 3 camera target is a texture, which Skia draws into through a Skia-side FBO |
| OpenGL ES (KNI Android) | Second EGL context sharing KNI's, created from the host's current `EGLConfig`; surfaceless where the driver allows it |
| OpenGL ES (MonoGame iOS) | Second `EAGLContext` in MonoGame's sharegroup; the back buffer is MonoGame's FBO around a `CAEAGLLayer` renderbuffer |
| OpenGL ES (KNI iOS) | Same as MonoGame iOS: second `EAGLContext` in KNI's sharegroup; KNI's back buffer is the same FBO around a `CAEAGLLayer` renderbuffer |
| OpenGL (raylib) | Second WGL (Windows), GLX (Linux) or CGL (macOS) context shares rlgl's GL namespace |
| OpenGL 3.3 (Godot Compatibility) | Second GL context sharing Godot's; Skia draws into an FBO around an `ImageTexture`'s GL texture, zero-copy |
| D3D11 (MonoGame/KNI/FNA WindowsDX, Stride, Unity) | ANGLE (GL ES → D3D11 translation) on the engine's shared device. FNA gets the device through `FNA3D_GetSysRendererEXT`; Unity uses the device its `RenderTexture` belongs to |
| D3D12 (MonoGame WindowsDX12, Stride, Godot, Unity) | Skia's D3D12 backend on the engine's `ID3D12Device`/queue. MonoGame wraps the target with `RenderTarget2D.FromNativeHandle()`; Godot draws into a typed resource and does one GPU `CopyResource` per frame (no readback, not zero-copy); Unity goes through a small native plugin |
| Vulkan (MonoGame DesktopVK, Stride, Godot, Unity) | Skia's Vulkan backend on the engine's `VkDevice`/`VkQueue`, no separate context. Godot's handles come from public `RenderingDevice.GetDriverResource`, no reflection; Unity's come from a small native plugin over `IUnityGraphicsVulkan` |
| Metal (Godot, Unity) | Skia's Metal backend on the engine's `MTLDevice`/queue, drawn straight into the engine's `MTLTexture`; Unity goes through a small native plugin |
| WebGL2 (KNI Blazor) | `SkiaRenderTarget2D`: cross-context `texSubImage2D(canvas)` from Skia's own canvas. `SkiaDirectCanvas`: KNI's WebGL2 context registered with SkiaSharp's Emscripten GL runtime, so Skia and KNI share one context and nothing is copied |

Unity records draws to an `SKPicture` on the main thread and plays them back on its render thread.

See [`SkiaGameRendering-Notes.md`](../SkiaGameRendering-Notes.md) for detailed technical documentation on how each backend works, including the ANGLE integration and D3D11 state management.

## What is verified where

- iOS (MonoGame and KNI) is verified on the CI iOS Simulator only. No physical iPhone or iPad, no GLES 2-only device.
- Android (KNI) is verified on two physical phones (GLES 3.2) and on emulators, with CI running the SwiftShader emulator. GLES 3.0-only drivers are not verified.
- Linux GPU paths, including Unity on Linux, are not verified.
- Unity is verified locally by the maintainer and never in CI, so a Unity change is only as checked as the last local run.
- Physical-device and tiled-GPU behavior (iOS, Android) is not pursued; only simulator and emulator runs count.
