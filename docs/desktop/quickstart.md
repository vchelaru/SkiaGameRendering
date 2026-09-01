# MonoGame / KNI desktop quick start

Covers the four "XNA-like" desktop backends: MonoGame DesktopGL, MonoGame WindowsDX, KNI
DesktopGL, KNI WindowsDX. All four share the same public API (`SkiaRenderer`,
`SkiaRenderTarget2D`) — only the package you install and the backend class differ.

## Prerequisites

- .NET 8, Visual Studio 2022
- MonoGame 3.8.4.1 (DesktopGL or WindowsDX) or KNI (DesktopGL or WindowsDX)
- SkiaSharp 3.119.4 for the KNI desktop backends; 3.119.2 for the MonoGame desktop projects

## Add the package

```powershell
dotnet add package SkiaGameRendering                    # MonoGame DesktopGL
dotnet add package SkiaGameRendering.WindowsDX           # MonoGame WindowsDX
dotnet add package SkiaGameRendering.Kni.DesktopGL       # KNI DesktopGL
dotnet add package SkiaGameRendering.Kni.WindowsDX       # KNI WindowsDX
```

## Initialize

No setup outside `Game` needed — `Program.cs` stays whatever the stock MonoGame/KNI template gives
you:

```cs
using var game = new Game1();
game.Run();
```

Inside `Game`, poll `SkiaRenderer.IsReady` before calling `SkiaRenderer.Initialize`, in `Draw()` (a
rendering-setup concern, colocated with the rendering that follows it) — the exact same code every
sample in this repo uses, desktop and web alike:

```cs
using SkiaGameRendering; // SkiaRenderer

protected override void Draw(GameTime gameTime)
{
    if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
        SkiaRenderer.Initialize(GraphicsDevice);

    if (SkiaRenderer.IsInitialized)
    {
        // normal draw logic
    }
    base.Draw(gameTime);
}
```

All four desktop backends' `IsReady` is `true` the instant `Draw()` first runs — their GL/D3D11
context exists as soon as the engine has created its `GraphicsDevice` — so `Initialize` fires on
the very first check; there's nothing to actually wait for. It's written this way (a poll, not a
direct call) so the *exact same* `Game` code also compiles and behaves correctly on KNI WebGL, where
`IsReady` reflects a real async host-readiness check instead of always being `true` (see
[the WebGL quick start](../webgl/quickstart.md)) — `Game` code never names a specific `SkiaBackend`
type on any platform, and `Initialize(GraphicsDevice)` auto-detects the right one via reflection.

Constructing a `SkiaRenderTarget2D` without ever calling `SkiaRenderer.Initialize` also
auto-detects and initializes the right backend for you (useful for quick scripts), but the
polling form above is what every sample uses and is the one to reach for by default.

## Render with SkiaRenderTarget2D

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into, sized to match whatever
you intend to draw it onto — typically the back buffer, or a `RenderTarget2D` the same size as the
viewport. Its Begin/End shape works like `SpriteBatch`'s own: place shapes at their own coordinates
via Skia's drawing API (the same way a `SpriteBatch.Draw` call carries its own position), and
`End()` composites the whole result onto whatever's currently bound — no separate
`spriteBatch.Draw(canvas.Texture, ...)` step:

```cs
using SkiaGameRendering; // SkiaRenderTarget2D
using SkiaSharp;          // SKPaint and the rest of the drawing API

var canvas = new SkiaRenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

// per frame:
canvas.Begin();
canvas.Canvas.DrawCircle(100, 100, 100, paint); // position is DrawCircle's job, not End's
canvas.End();
```

Positioning many separate shapes (e.g. one circle per game entity) works the same way `SpriteBatch`
batches many sprites: one shared canvas, `Begin()` once, draw each shape at its own coordinates,
`End()` once.

`End()` always composites at native size and the origin. If you need to draw the result more than
once, at a different size, or sample it in a shader, call `EndWithoutDrawing()` instead and
composite `.Texture` yourself — see [SkiaRenderTarget2D](../documentation/SkiaRenderTarget2D.md)
for a worked example.

Size is fixed for the object's lifetime, like `RenderTarget2D` — construct a new
`SkiaRenderTarget2D` (and `Dispose()` the old one) if you need a different size. Set a render
target before calling `Begin()` if you want the composite to land somewhere other than the back
buffer.

Tear the shared backend down (e.g. on exit, or before switching backends) with
`SkiaRenderer.Dispose()`. Dispose your own `SkiaRenderTarget2D` instances first — this doesn't
track or dispose them for you.

## Backends

| Backend | Engine/platform | How it works | Reference |
| --- | --- | --- | --- |
| `SkiaGlBackend` | MonoGame DesktopGL | Second SDL GL context sharing MonoGame's GL namespace | [docs](../documentation/SkiaGlBackend.md) |
| `SkiaKniGlBackend` | KNI DesktopGL | Same trick, adapted to KNI's SDL bridge | [docs](../documentation/SkiaKniGlBackend.md) |
| `SkiaAngleBackend` | MonoGame WindowsDX | ANGLE (GL ES → D3D11) on MonoGame's own D3D11 device | [docs](../documentation/SkiaAngleBackend.md) |
| `SkiaKniAngleBackend` | KNI WindowsDX | Same ANGLE interop, on KNI's D3D11 device via its Strategy bridge | [docs](../documentation/SkiaKniAngleBackend.md) |

The two GL backends share `src/SkiaGameRendering.Core.OGL/` (engine-agnostic raw-GL/Skia FBO
interop); the two ANGLE backends share `src/SkiaGameRendering.Core.ANGLE/` (engine-agnostic
D3D11/ANGLE interop). Because `MonoGame.Framework.DesktopGL` and `MonoGame.Framework.WindowsDX`
are separate NuGet packages that can't coexist, each backend still ships as its own library
project, with core source files shared via linked includes (`src/SkiaGameRendering/`,
`src/SkiaGameRendering.WindowsDX/`, `src/SkiaGameRendering.Kni.DesktopGL/`,
`src/SkiaGameRendering.Kni.WindowsDX/`).

## Known limitations

- **ANGLE DLL packaging**: the WindowsDX backends currently fall back to Edge WebView's system
  ANGLE DLLs. The `Silk.NET.OpenGLES.ANGLE.Native` NuGet package ships 32-bit DLLs mislabeled as
  x64, so it can't be used as-is.
- **`glFinish` per renderable**: the ANGLE backends call `glFinish()` for GPU sync on every
  render-target pass. This could potentially be relaxed to `glFlush()` if D3D11's own
  synchronization turns out to be sufficient — unverified.
- Per-backend reflection fragility (which internal fields each backend depends on, and how narrow
  that surface is) is documented on each backend's own reference page above.

## Sample projects

- `samples/Sample.MonoGame.DesktopGL/` — cross-platform: Windows, Linux, macOS
- `samples/Sample.MonoGame.WindowsDX/` — Windows only
- `samples/Sample.Kni.DesktopGL/` — cross-platform: Windows, Linux, macOS
- `samples/Sample.Kni.WindowsDX/` — Windows only

DesktopGL, WindowsDX, and KNI WindowsDX share the same `Game1.cs` via a linked file include; KNI
DesktopGL has its own copy.
