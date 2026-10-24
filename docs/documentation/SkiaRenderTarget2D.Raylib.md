# SkiaRenderTarget2D

## Definition

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into. It has the MonoGame type's Begin/Canvas/End shape — `End()` composites the whole surface itself via
`Raylib.DrawTexture` — but is a standalone class: raylib support does **not** go through
`SkiaBackend`. That
type is typed directly to MonoGame's `Texture2D`, and raylib is the first non-MonoGame engine
this library supports, so `src/SkiaGameRendering.Raylib.OGL/` is a hand-rolled sibling typed to
raylib's own `Texture2D` throughout, reusing the same engine-agnostic GL/Skia FBO interop
(`Core.OGL`) that `SkiaGlBackend` uses.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering.Raylib.OGL`

```csharp
public sealed class SkiaRenderTarget2D : IDisposable
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaRenderTarget2D(int width, int height, SKColorType colorType = SKColorType.Rgba8888)` | Auto-initializes `SkiaRenderer` on first use and allocates a fixed-size target. |

## Members

| Member | Description |
| --- | --- |
| `Texture` | The raylib `Texture2D`. Mainly useful alongside `EndWithoutDrawing`. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`/`EndWithoutDrawing`. |
| `Begin(bool clear = true)` | Switches to the shared Skia GL context and binds this target's FBO. |
| `End()` | Flushes Skia's queued GPU work, switches back to raylib's own GL context, and composites the whole surface at native size and the origin via `Raylib.DrawTexture` — call between `Raylib.BeginDrawing()`/`EndDrawing()`, same as any other raylib draw call. |
| `EndWithoutDrawing()` | Same as `End`, but skips the composite. |
| `Dispose()` | Releases the texture and Skia surface. Must not be called between `Begin`/`End`. |

## Related types

| Type | Role |
| --- | --- |
| `SkiaRenderer` | Static holder for the shared `SkiaRaylibContext`, a static class in the same namespace as the MonoGame/KNI `SkiaRenderer`. `Initialize()` is optional — call it explicitly (after `Raylib.InitWindow`) only to fail fast on context creation instead of lazily on first render target. `Dispose()` releases it. |
| `SkiaRaylibContext` | The shared second GL context. rlgl (raylib's GL layer) and Skia both issue raw GL calls; sharing raylib's single context between them corrupts rlgl's own rendering, so this creates a second context sharing raylib's GL object namespace (the same trick `SkiaGlBackend` uses via SDL). raylib statically links GLFW and doesn't export context-creation entry points, so this goes through the OS's raw windowing API directly: `Core.OGL`'s `WglSharedContext` (raw Win32/WGL) on Windows and `GlxSharedContext` (raw X11/GLX) on Linux, picked via `OperatingSystem.IsWindows()`/`IsLinux()`. The Godot adapter's Compatibility backend uses the same two classes. |

## Example

```csharp
using Raylib_cs;
using SkiaGameRendering;
using SkiaSharp;

Raylib.InitWindow(800, 600, "raylib + Skia");
SkiaRenderer.Initialize();

var canvas = new SkiaRenderTarget2D(800, 600);
using var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

while (!Raylib.WindowShouldClose())
{
    Raylib.BeginDrawing();

    canvas.Begin();
    canvas.Canvas.Clear(SKColors.CornflowerBlue);
    canvas.Canvas.DrawRect(SKRect.Create(300, 200, 200, 200), paint);
    canvas.End(); // composites itself via Raylib.DrawTexture

    Raylib.EndDrawing();
}

canvas.Dispose();
SkiaRenderer.Dispose();
Raylib.CloseWindow();
```

## Remarks

- `Begin`/`End` must run between `Raylib.BeginDrawing()`/`EndDrawing()`, since `End()`'s composite
  is itself a `Raylib.DrawTexture` call. If you need the raw texture without an immediate
  composite (drawing it more than once, or at a different size), call `EndWithoutDrawing()` and
  draw `Texture` yourself.
- Platform support: Windows and Linux (Linux verified under WSLg — X11/GLX, Mesa llvmpipe). macOS
  is not implemented.
- raylib only supports a single window, so `SkiaRenderer` holds one global context rather
  than keying off a window handle the way the MonoGame `SkiaRenderer` keys off a `GraphicsDevice`.
  `SkiaRaylibContext.Initialize` must run after `Raylib.InitWindow` — it reads the window handle
  via `Raylib.GetWindowHandle()`, which is only valid once the window exists.
- The Skia surface is created with `GRSurfaceOrigin.BottomLeft` (the same
  `GlSkiaSurfaceFactory.CreateSurface` used by the MonoGame/KNI GL backends, with an optional
  origin parameter that defaults to `TopLeft` for them), so `Texture` already matches raylib's
  bottom-left texture-sampling convention — no manual flip needed.
- On Linux, `SkiaSharp.NativeAssets.Linux` must be referenced (the main `SkiaSharp` package only
  carries Windows/macOS native assets implicitly); without it `GRContext.CreateGl()` throws
  `DllNotFoundException` for `libSkiaSharp.so` at runtime even though the build succeeds.
- Dispose order matters: dispose your `SkiaRenderTarget2D` instances before calling
  `SkiaRenderer.Dispose()`, which tears down the GL context they depend on.
- See also the [raylib quick start](../raylib/quickstart.md).
