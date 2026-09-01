# Skia Game Rendering

A library that lets MonoGame and KNI applications use SkiaSharp's GPU rendering to produce `Texture2D`s — with zero-copy GPU texture sharing. Skia renders anti-aliased vector art, text, and 2D graphics directly into game-engine textures without any CPU readback.

## Platform Support

| Platform | Backend | Status | How it works |
|----------|---------|--------|--------------|
| MonoGame 3.8.4 DesktopGL | OpenGL | Working | Shared GL context via SDL |
| MonoGame 3.8.4 WindowsDX | D3D11 | Working | ANGLE (GL ES → D3D11 translation) on shared device |
| MonoGame 3.8.5 DirectX | D3D11/D3D12 | Not started | |
| MonoGame 3.8.5 Vulkan | Vulkan | Not started | |
| KNI DesktopGL | OpenGL | Not started | |
| KNI WindowsDX | D3D11 | Working | ANGLE (GL ES → D3D11 translation) on shared device |
| KNI Android | GL ES | Not started | |
| KNI WebGL (Blazor) | WebGL2 | Production candidate | Cross-context `texSubImage2D(canvas)` through KNI's stock public API |
| raylib | OpenGL | Working (Windows + Linux) | Second WGL (Windows) or GLX (Linux) context shares rlgl's GL namespace |

## Requirements

- .NET 8
- Visual Studio 2022
- MonoGame 3.8.4.1 (DesktopGL or WindowsDX) or KNI (WebGL/Blazor or WindowsDX)
- SkiaSharp 3.119.4 for WebGL and KNI WindowsDX; 3.119.2 for the MonoGame desktop projects

## Quick Start

Install the NuGet package for your platform into an existing MonoGame/KNI project:
- **MonoGame DesktopGL**: `dotnet add package SkiaGameRendering`
- **MonoGame WindowsDX**: `dotnet add package SkiaGameRendering.WindowsDX`
- **KNI WebGL (Blazor)**: `dotnet add package SkiaGameRendering.Kni.WebGL` — needs a couple of extra setup steps beyond the package install; see `docs/webgl/quickstart.md`.
- **raylib**: `dotnet add package SkiaGameRendering.Raylib`

No explicit setup call is required for the common case — constructing a `SkiaRenderTarget2D`
auto-detects and initializes the right backend for the `GraphicsDevice` you pass it the first time
it's needed. To force a specific backend instead of auto-detection (e.g. in tests), call this once
before constructing any `SkiaRenderTarget2D`:
```cs
using SkiaGameRendering; // SkiaRenderer, SkiaGlBackend, SkiaAngleBackend
using SkiaGameRendering.Kni.WindowsDX; // SkiaKniAngleBackend

SkiaRenderer.Initialize(new SkiaGlBackend(), GraphicsDevice);        // MonoGame DesktopGL
SkiaRenderer.Initialize(new SkiaAngleBackend(), GraphicsDevice);     // MonoGame WindowsDX
SkiaRenderer.Initialize(new SkiaKniAngleBackend(), GraphicsDevice);  // KNI WindowsDX
```

## SkiaRenderTarget2D

`SkiaRenderTarget2D` is a fixed-size GPU texture that SkiaSharp renders directly into — construct it
once, `Begin()`/draw/`End()` per frame (mirroring `SpriteBatch`'s own shape), then hand `.Texture` to
`SpriteBatch` like any other texture:

```cs
using SkiaGameRendering; // SkiaRenderTarget2D, and the SpriteBatch.Draw(canvas, ...) extension overload
using SkiaSharp;          // SKPaint and the rest of the drawing API

var canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);

// per frame:
canvas.Begin();
canvas.Canvas.DrawCircle(100, 100, 100, paint);
canvas.End();

spriteBatch.Draw(canvas.Texture, position, Color.White);   // works directly
spriteBatch.Draw(canvas, position, Color.White);           // or via the SpriteBatch extension methods
```

| Member | Description |
|--------|-------------|
| `Texture` | The `Texture2D` to draw with `SpriteBatch` |
| `Canvas` | The `SKCanvas` to draw on — only valid between `Begin()` and `End()`; throws otherwise |
| `Begin(bool clear = true)` | Starts a render pass; throws if called again before `End()` |
| `End()` | Ends the render pass; throws if `Begin()` wasn't called first |
| `Dispose()` | Releases the underlying GPU resources; throws if called between `Begin()` and `End()` |

Size is fixed for the object's lifetime, like `RenderTarget2D` — construct a new
`SkiaRenderTarget2D` (and `Dispose()` the old one) if you need a different size.

Tear the shared backend down (e.g. on exit, or before switching backends) with:
```cs
SkiaRenderer.Dispose();
```
Dispose your own `SkiaRenderTarget2D` instances first — this doesn't track or dispose them for you.

## Sample Projects

- `samples/Sample.MonoGame.DesktopGL/` — DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `samples/Sample.MonoGame.WindowsDX/` — WindowsDX sample (Windows only)
- `samples/Sample.Kni.WindowsDX/` — KNI WindowsDX sample (Windows only)
- `samples/Sample.Kni.WebGL/` — KNI Blazor WebAssembly sample using the patched canvas-upload API
- `samples/Sample.Raylib/` — raylib sample (Windows + Linux)
- `samples/Test/` — More comprehensive test with dynamic add/remove, FPS counter, input handling

DesktopGL, WindowsDX, and KNI WindowsDX share the same `Game1.cs` via a linked file include.

## Architecture

The library uses a backend abstraction (`SkiaBackend` base class) so each graphics API gets its own implementation. Core source files are shared across platform-specific library projects via linked includes:

- `src/SkiaGameRendering/` — DesktopGL library (core + `SkiaGlBackend`)
- `src/SkiaGameRendering.Core.OGL/` — engine-agnostic raw-GL/Skia FBO interop shared by GL-based backends
- `src/SkiaGameRendering.Core.ANGLE/` — engine-agnostic D3D11/ANGLE interop shared by ANGLE-based backends
- `src/SkiaGameRendering.WindowsDX/` — MonoGame WindowsDX library (shared core + `SkiaAngleBackend`, on `Core.ANGLE`)
- `src/SkiaGameRendering.Kni.WindowsDX/` — KNI WindowsDX library (shared core + `SkiaKniAngleBackend`, on `Core.ANGLE`)
- `src/SkiaGameRendering.Kni.WebGL/` — KNI/Blazor library (shared core + `SkiaWebGlBackend`)
- `src/SkiaGameRendering.Raylib/` — raylib library (shared `Core.OGL` + `SkiaRaylibRenderTarget2D`)

See `SkiaGameRendering-Notes.md` for detailed technical documentation on how each backend works, including the ANGLE integration and D3D11 state management.

## WebGL / WASM Status

The WebGL backend is implemented in `SkiaGameRendering.Kni.WebGL`. It creates a synchronous SkiaSharp WebGL2 source host, flushes the current Skia frame, and uploads that canvas directly into a preallocated KNI `Texture2D` with `texSubImage2D`. Production code has no `readPixels`, managed pixel buffer, or `Texture2D.SetData(byte[])` path.

The browser backend consumes stock KNI from NuGet (no fork or patch); the one internal (the current WebGL rendering context) that KNI doesn't expose publicly is reached via reflection instead — see `src/SkiaGameRendering.Kni.WebGL/WebGlCanvasUpload.cs`.

```powershell
dotnet workload install wasm-tools-net8
dotnet build samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release
dotnet run --project samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release --no-build
```

The sample proves SpriteBatch interleaving, render-target consumption, shader sampling, animated Gum/Skia content, pointer/touch/wheel/text input, fractional DPR handling, and backend recreation. See `docs/webgl/quickstart.md`, `docs/webgl/validated-baseline.md`, and `docs/documentation/SkiaWebGlBackend.md` for the exact contract and support status.

## Using SkiaSharp

Between `Begin()` and `End()`, `SkiaRenderTarget2D.Canvas` gives you a full GPU-accelerated
`SKCanvas`. For example, drawing an anti-aliased circle:

```cs
canvas.Begin();
canvas.Canvas.DrawCircle(Radius, Radius, Radius, _paint);
canvas.End();
```

For more on SkiaSharp drawing, see the [SkiaSharp documentation](https://learn.microsoft.com/en-us/previous-versions/xamarin/xamarin-forms/user-interface/graphics/skiasharp/basics/).

## License

MIT License. See [LICENSE.md](LICENSE.md).

## Credits

Originally created by [Miguel Anxo Figueirido](https://github.com/mfigueirido/SkiaMonoGameRendering). Multi-platform backend abstraction and WindowsDX/ANGLE support added by Victor Chelaru.
