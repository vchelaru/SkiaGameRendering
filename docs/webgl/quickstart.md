# KNI WebGL quick start

## Prerequisites

- .NET 8 SDK and the `wasm-tools-net8` workload
- WebGL2-capable browser

## Build and run

```powershell
dotnet workload install wasm-tools-net8
dotnet build samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release
dotnet run --project samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release --no-build
```

The library is a Razor class library, so `skia-monogame-webgl.js` is delivered as a static web asset. Consumers do not copy JavaScript manually.

## Application contract

Render `<SkiaMonoGameWebGlHost @ref="host" />`, await `host.Ready`, then construct `SkiaWebGlBackend` explicitly and initialize `SkiaRenderer`. Construct one `SkiaRenderTarget2D` per Skia surface you need. All graphics calls must stay on the browser graphics thread.

A `SkiaRenderTarget2D`'s `Begin()`/`Canvas`/`End()` must run after any SpriteBatch pass below the UI has ended. `.Texture` is a normal KNI `Texture2D` and can be sampled by SpriteBatch, a `RenderTarget2D`, or an effect.

Dispose in this order: stop the game loop, dispose your `SkiaRenderTarget2D` instances, call `SkiaRenderer.Dispose()` to release the backend, then dispose the host.

## Packages

`Sample.Kni.WebGL` consumes stock KNI from NuGet (`nkast.Kni.Platform.Blazor.GL`, pinned via `eng/Versions.props`'s `KniVersion`) — no fork or source patch. Getting the destination `WebGLTexture`/rendering-context handles KNI doesn't expose publicly (see `WebGlCanvasUpload.cs`'s `KniWebGlInternals`) is done via reflection into KNI internals instead. `dotnet pack src/SkiaMonoGameRendering.Kni.WebGL/SkiaMonoGameRendering.Kni.WebGL.csproj -c Release -o .artifacts/packages` builds just this repo's own package.
