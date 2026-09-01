# Skia Game Rendering

A library that lets MonoGame and KNI applications use SkiaSharp's GPU rendering to produce `Texture2D`s — with zero-copy GPU texture sharing. Skia renders anti-aliased vector art, text, and 2D graphics directly into game-engine textures without any CPU readback.

## Platform Support

| Platform | Backend | Status | How it works |
|----------|---------|--------|--------------|
| MonoGame 3.8.4 DesktopGL | OpenGL | Working | Shared GL context via SDL |
| MonoGame 3.8.4 WindowsDX | D3D11 | Working | ANGLE (GL ES → D3D11 translation) on shared device |
| MonoGame 3.8.5 DirectX | D3D11/D3D12 | Not started | |
| MonoGame 3.8.5 Vulkan | Vulkan | Not started | |
| KNI DesktopGL | OpenGL | Working | Shared GL context via SDL |
| KNI WindowsDX | D3D11 | Working | ANGLE (GL ES → D3D11 translation) on shared device |
| KNI Android | GL ES | Not started | |
| KNI WebGL (Blazor) | WebGL2 | Production candidate | Cross-context `texSubImage2D(canvas)` through KNI's stock public API |
| raylib | OpenGL | Working (Windows + Linux) | Second WGL (Windows) or GLX (Linux) context shares rlgl's GL namespace |

## Requirements

- .NET 8
- Visual Studio 2022
- MonoGame 3.8.4.1 (DesktopGL or WindowsDX) or KNI (DesktopGL, WindowsDX, or WebGL/Blazor)
- SkiaSharp 3.119.4 for WebGL and the KNI desktop backends; 3.119.2 for the MonoGame desktop projects

## Quick Start

Install the NuGet package for your platform into an existing MonoGame/KNI project (see
`docs/desktop/quickstart.md` for a full walkthrough of the four backends below):
- **MonoGame DesktopGL**: `dotnet add package SkiaGameRendering`
- **MonoGame WindowsDX**: `dotnet add package SkiaGameRendering.WindowsDX`
- **KNI DesktopGL**: `dotnet add package SkiaGameRendering.Kni.DesktopGL`
- **KNI WindowsDX**: `dotnet add package SkiaGameRendering.Kni.WindowsDX`
- **KNI WebGL (Blazor)**: `dotnet add package SkiaGameRendering.Kni.WebGL` — needs a couple of extra setup steps beyond the package install; see `docs/webgl/quickstart.md`.
- **raylib**: `dotnet add package SkiaGameRendering.Raylib` — see `docs/raylib/quickstart.md`.

Construct the backend in `Program.cs`, `await` its `Ready` (a no-op on every desktop backend, but
identical to the KNI WebGL sample's shape — see `docs/webgl/quickstart.md`), and pass it into your
`Game`, whose `Initialize()` calls `SkiaRenderer.Initialize`:
```cs
using SkiaGameRendering; // SkiaBackend, SkiaRenderer, SkiaGlBackend, SkiaAngleBackend

var backend = new SkiaGlBackend();     // MonoGame DesktopGL - or SkiaAngleBackend (WindowsDX),
                                        // SkiaKniGlBackend (KNI DesktopGL), SkiaKniAngleBackend (KNI WindowsDX)
await backend.Ready;
using var game = new Game1(backend);
game.Run();

// In Game1:
public Game1(SkiaBackend backend) => _backend = backend;

protected override void Initialize()
{
    SkiaRenderer.Initialize(_backend, GraphicsDevice);
    base.Initialize();
}
```

Constructing a `SkiaRenderTarget2D` without ever calling `SkiaRenderer.Initialize` also
auto-detects and initializes the right backend for you — useful for quick scripts — but the
explicit form above is what every sample in this repo uses, so it's the one to reach for by
default.

## SkiaRenderTarget2D

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into, sized to match whatever
you intend to draw it onto (typically the back buffer, or a `RenderTarget2D` the same size as the
viewport). Its Begin/End shape works like `SpriteBatch`'s own: place individual shapes with their
own coordinates via Skia's drawing API — the same way a `SpriteBatch.Draw` call carries its own
position — and `End()` composites the whole result onto whatever render target is currently bound,
the same way `SpriteBatch.End()` needs no separate step to show its queued sprite draws:

```cs
using SkiaGameRendering; // SkiaRenderTarget2D
using SkiaSharp;          // SKPaint and the rest of the drawing API

var canvas = new SkiaRenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

// per frame:
canvas.Begin();
canvas.Canvas.DrawCircle(100, 100, 100, paint); // position is DrawCircle's job, not End's
canvas.End();
```

| Member | Description |
|--------|-------------|
| `Texture` | The underlying `Texture2D`, mainly useful with `EndWithoutDrawing` |
| `Canvas` | The `SKCanvas` to draw on — only valid between `Begin()` and `End()`; throws otherwise |
| `Begin(bool clear = true)` | Starts a render pass; throws if called again before `End()` |
| `End()` | Ends the render pass and composites the whole surface, at native size and the origin, onto whatever's currently bound; throws if `Begin()` wasn't called first |
| `EndWithoutDrawing()` | Same as `End()`, but skips the composite — use only when `End()`'s single whole-surface blit can't express what you need (drawing it more than once, at a different size/position, or sampling it in a shader). You then draw `Texture` yourself. |
| `Dispose()` | Releases the underlying GPU resources; throws if called between `Begin()` and `End()` |

Size is fixed for the object's lifetime, like `RenderTarget2D` — construct a new
`SkiaRenderTarget2D` (and `Dispose()` the old one) if you need a different size. Set a render
target before calling `Begin()` if you want `End()`'s composite to land somewhere other than the
back buffer.

Tear the shared backend down (e.g. on exit, or before switching backends) with:
```cs
SkiaRenderer.Dispose();
```
Dispose your own `SkiaRenderTarget2D` instances first — this doesn't track or dispose them for you.

## Sample Projects

- `samples/Sample.MonoGame.DesktopGL/` — DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `samples/Sample.MonoGame.WindowsDX/` — WindowsDX sample (Windows only)
- `samples/Sample.Kni.DesktopGL/` — KNI DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `samples/Sample.Kni.WindowsDX/` — KNI WindowsDX sample (Windows only)
- `samples/Sample.Kni.WebGL/` — KNI Blazor WebAssembly sample using the patched canvas-upload API
- `samples/Sample.Raylib/` — raylib sample (Windows + Linux)
- `samples/Test/` — More comprehensive test with dynamic add/remove, FPS counter, input handling

DesktopGL, WindowsDX, and KNI WindowsDX share the same `Game1.cs` via a linked file include; KNI DesktopGL has its own copy.

## Architecture

The library uses a backend abstraction (`SkiaBackend` base class) so each graphics API gets its own implementation. Core source files are shared across platform-specific library projects via linked includes:

- `src/SkiaGameRendering/` — DesktopGL library (core + `SkiaGlBackend`)
- `src/SkiaGameRendering.Core.OGL/` — engine-agnostic raw-GL/Skia FBO interop shared by GL-based backends
- `src/SkiaGameRendering.Core.ANGLE/` — engine-agnostic D3D11/ANGLE interop shared by ANGLE-based backends
- `src/SkiaGameRendering.WindowsDX/` — MonoGame WindowsDX library (shared core + `SkiaAngleBackend`, on `Core.ANGLE`)
- `src/SkiaGameRendering.Kni.DesktopGL/` — KNI DesktopGL library (shared core + `SkiaKniGlBackend`)
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
