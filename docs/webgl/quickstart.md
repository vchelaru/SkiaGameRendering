# KNI WebGL quick start

## Prerequisites

- .NET 8 SDK and the `wasm-tools-net8` workload (`dotnet workload install wasm-tools-net8`)
- WebGL2-capable browser

## New project

If you don't already have a KNI Blazor project, scaffold one from KNI's own official template:

```powershell
dotnet new install nkast.Kni.Templates
dotnet new kni-blazor-gl -n MyGame
```

That gives you a working KNI `Game`/`GraphicsDeviceManager` running in a Blazor WebAssembly page, with no Skia involved yet.

## Add the package

```powershell
dotnet add package SkiaGameRendering.Kni.WebGL
```

This is a Razor class library — `skia-game-webgl.js` and the other static web assets it needs come along automatically. Nothing to copy by hand.

## Application contract

Render `<SkiaGameWebGlHost @ref="host" />` on the page, await `host.Ready`, then construct `SkiaWebGlBackend` explicitly and initialize `SkiaRenderer`:

```cs
await host.Ready;
var backend = new SkiaWebGlBackend(host);
SkiaRenderer.Initialize(backend, GraphicsDevice);
```

Construct one `SkiaRenderTarget2D` per Skia surface you need. All graphics calls must stay on the browser graphics thread.

A `SkiaRenderTarget2D`'s `Begin()`/`Canvas`/`End()` must run after any SpriteBatch pass below the UI has ended. `.Texture` is a normal KNI `Texture2D` and can be sampled by SpriteBatch, a `RenderTarget2D`, or an effect.

Dispose in this order: stop the game loop, dispose your `SkiaRenderTarget2D` instances, call `SkiaRenderer.Dispose()` to release the backend, then dispose the host.

## Build and run the sample in this repo

If you're working from a clone of this repo rather than a fresh project:

```powershell
dotnet workload install wasm-tools-net8
dotnet build samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release
dotnet run --project samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release --no-build
```

The sample proves SpriteBatch interleaving, render-target consumption, shader sampling, animated Gum/Skia content, pointer/touch/wheel/text input, fractional DPR handling, and backend recreation.

## Packages

`SkiaGameRendering.Kni.WebGL` consumes stock KNI from NuGet (`nkast.Kni.Platform.Blazor.GL`) — no fork or source patch. Getting the destination `WebGLTexture`/rendering-context handles KNI doesn't expose publicly (see `WebGlCanvasUpload.cs`'s `KniWebGlInternals`) is done via reflection into KNI internals instead.
