# raylib WebGL quick start

raylib in the browser (`PLATFORM_WEB`, WebGL 2) from .NET WebAssembly. `SkiaRenderTarget2D`, `SkiaDirectCanvas` and
`SkiaRenderer` have the same names, namespace and Begin/Canvas/End shape as the [desktop raylib package](quickstart.md).

## How it works

raylib and SkiaSharp are both linked into the app's `dotnet.native.wasm`, so they share one Emscripten WebGL
runtime. Skia does not get a context of its own (WebGL contexts cannot share objects): it draws on the context raylib
created. Each pass saves raylib's GL state, lets Skia draw, and restores it. No CPU readback is involved.

## Prerequisites

- .NET 10 SDK and the wasm workload: `dotnet workload install wasm-tools` (net8 is not supported: Raylib-cs's browser-wasm raylib only works on net10)
- A Blazor WebAssembly project (`Microsoft.NET.Sdk.BlazorWebAssembly`) with `<canvas id="canvas">` on the page
- [Raylib-cs](https://www.nuget.org/packages/Raylib-cs) 8.1.0 (comes with the package)

## Add the package

```powershell
dotnet add package SkiaGameRendering.Raylib.WebGL
```

raylib comes from Raylib-cs's own browser-wasm `raylib.a`, which its build targets link into your app when
`RuntimeIdentifier` is `browser-wasm`; the package adds a pre-js. No script tags. That raylib is its OpenGL ES 2
build (no vertex array objects, no non-power-of-two textures), so the console shows `vertexAttribPointer: index out of range` warnings from it, and raylib may log that the VAO and NPOT extensions are not found; they are harmless. raylib keeps its vertex attributes on WebGL's default vertex array, Skia overwrites them on each pass, and the package's `skia-raylib-webgl.js` puts them back afterward.

Editing the pre-js (`skia-raylib-webgl.js`) does not make Emscripten relink. If a change to it has no effect, delete the `wasm` folder under your project's `obj` and build again.

## Initialize and render

The browser has no blocking main loop, so drive frames from `requestAnimationFrame` (see
`samples/Sample.Raylib.WebGL`):

```cs
Raylib.InitWindow(800, 600, "raylib + Skia");   // creates the WebGL context on <canvas id="canvas">
SkiaRenderer.Initialize();                       // optional, after InitWindow
var canvas = new SkiaRenderTarget2D(800, 600);
var direct = new SkiaDirectCanvas();

[JSInvokable] // called from requestAnimationFrame
public static void Frame()
{
    Raylib.BeginDrawing();
    Raylib.ClearBackground(Color.Black);

    canvas.Begin();
    canvas.Canvas.DrawCircle(100, 100, 50, paint);
    canvas.End();                                // draws the texture with Raylib.DrawTexture

    direct.Begin();                              // straight into the canvas, or into BeginTextureMode's target
    direct.Canvas.DrawRect(200, 20, 80, 120, paint);
    direct.End();

    Raylib.EndDrawing();
}
```

A different canvas id: set `globalThis.skiaRaylibCanvasId` before `Blazor.start()`.

## Limitations

- `SkiaRenderTarget2D` supports `SKColorType.Rgba8888` only.
- A Skia pass cannot be nested in another (`SkiaRenderTarget2D` and `SkiaDirectCanvas` passes are exclusive).
- Context loss is not recovered. raylib's resources die with the context, so every pass throws `InvalidOperationException` from then on, even if the browser restores the context; close the window and start a new one.
- Verified in Chromium, Firefox and WebKit (headless, software GL).
