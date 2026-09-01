# raylib quick start

raylib is the first engine this library supports that isn't MonoGame/KNI. It does not go through
`SkiaBackend`/`SkiaRenderer` — see [SkiaRaylibRenderTarget2D](../documentation/SkiaRaylibRenderTarget2D.md)
for why and how the raylib support is structured differently.

## Prerequisites

- .NET 8
- [Raylib-cs](https://www.nuget.org/packages/Raylib-cs) 8.0.0
- Windows or Linux (Linux verified under WSLg — X11/GLX, Mesa llvmpipe). macOS is not implemented.

## Add the package

```powershell
dotnet add package SkiaGameRendering.Raylib
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
using SkiaGameRendering.Raylib;
using SkiaSharp;

Raylib.InitWindow(800, 600, "raylib + Skia");
Raylib.SetTargetFPS(60);

// Optional — SkiaRaylibRenderTarget2D auto-initializes on first use. Call this explicitly only to
// fail fast (with a clear stack) if the shared GL context can't be created, instead of surfacing
// the failure lazily on first draw. Must run after Raylib.InitWindow.
SkiaRaylibRenderer.Initialize();

var canvas = new SkiaRaylibRenderTarget2D(800, 600);
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
SkiaRaylibRenderer.Dispose();   // ...tearing down the shared GL context
Raylib.CloseWindow();
```

Full member list, the WGL/GLX context-sharing mechanism, and other remarks are documented on
[SkiaRaylibRenderTarget2D](../documentation/SkiaRaylibRenderTarget2D.md).

## Build and run the sample in this repo

```powershell
dotnet build samples\Sample.Raylib\Sample.Raylib.csproj -c Release
dotnet run --project samples\Sample.Raylib\Sample.Raylib.csproj -c Release --no-build
```

## Known limitations

- macOS is not implemented.
- Only one raylib window is supported per process — `SkiaRaylibRenderer` holds a single global
  context rather than keying off a window handle.
