# raylib quick start

raylib is the first engine this library supports that isn't MonoGame/KNI. Its package has
`SkiaRenderTarget2D`, `SkiaDirectCanvas` and `SkiaRenderer` under the same names and namespace
(`SkiaGameRendering`) as the MonoGame/KNI ones, with raylib's own constructors; see the
[raylib SkiaRenderTarget2D page](../documentation/SkiaRenderTarget2D.md#raylib).

## Prerequisites

- .NET 8
- [Raylib-cs](https://www.nuget.org/packages/Raylib-cs) 8.0.0
- Windows or Linux (Linux verified under WSLg — X11/GLX, Mesa llvmpipe). macOS is not implemented.

## Add the package

```powershell
dotnet add package SkiaGameRendering.Raylib.OGL
```

On Linux, also add `SkiaSharp.NativeAssets.Linux` — the main `SkiaSharp` package only carries
Windows/macOS native assets implicitly. Without it, `GRContext.CreateGl()` throws
`DllNotFoundException` for `libSkiaSharp.so` at runtime even though the build succeeds.

```powershell
dotnet add package SkiaSharp.NativeAssets.Linux
```

## Initialize and render

```cs
using Raylib_cs;
using SkiaGameRendering;
using SkiaSharp;

Raylib.InitWindow(800, 600, "raylib + Skia");
Raylib.SetTargetFPS(60);

// Optional — SkiaRenderTarget2D auto-initializes on first use. Call this explicitly only to
// fail fast (with a clear stack) if the shared GL context can't be created, instead of surfacing
// the failure lazily on first draw. Must run after Raylib.InitWindow.
SkiaRenderer.Initialize();

var canvas = new SkiaRenderTarget2D(800, 600);
using var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

while (!Raylib.WindowShouldClose())
{
    Raylib.BeginDrawing();

    // End() composites the whole surface itself, via Raylib.DrawTexture at (0,0) - call Begin/End
    // between BeginDrawing/EndDrawing, same as any other raylib draw call.
    canvas.Begin();
    canvas.Canvas.Clear(SKColors.CornflowerBlue);
    canvas.Canvas.DrawRect(SKRect.Create(300, 200, 200, 200), paint);
    canvas.End();

    Raylib.EndDrawing();
}

canvas.Dispose();               // dispose render targets before...
SkiaRenderer.Dispose();   // ...tearing down the shared GL context
Raylib.CloseWindow();
```

To draw straight into the back buffer or the bound `RenderTexture2D`, with no texture and no
composite, use `SkiaDirectCanvas` instead; see [SkiaDirectCanvas](../documentation/SkiaDirectCanvas.md).
The sample toggles between the two with D.

Full member list, the WGL/GLX context-sharing mechanism, and other remarks are in the
[raylib SkiaRenderTarget2D page](../documentation/SkiaRenderTarget2D.md#raylib).

## Build and run the sample in this repo

```powershell
dotnet build samples\Sample.Raylib.OGL\Sample.Raylib.OGL.csproj -c Release
dotnet run --project samples\Sample.Raylib.OGL\Sample.Raylib.OGL.csproj -c Release --no-build
```

## Known limitations

- macOS is not implemented.
- Only one raylib window is supported per process — `SkiaRenderer` holds a single global
  context rather than keying off a window handle.
