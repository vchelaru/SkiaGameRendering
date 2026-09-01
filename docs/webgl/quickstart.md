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

Add these usings — to `_Imports.razor` for the `.razor` markup, and separately to your page's `.razor.cs` code-behind, since `_Imports.razor` only reaches Razor-compiled markup, not the plain C# code-behind file:

```cs
using SkiaGameRendering;
using SkiaGameRendering.Kni.WebGL;
using SkiaGameRendering.Kni.WebGL.Components;
```

Render `<SkiaGameWebGlHost @ref="host" />` on the page. `GraphicsDevice` isn't available on the Blazor page itself — it belongs to your `Game` instance — so `SkiaWebGlBackend` gets constructed on the page, but `SkiaRenderer.Initialize` doesn't run until your `Game`'s own `Initialize()` override, where `GraphicsDevice` is in scope. That split is what every `SkiaBackend` needs: **construct the backend, await its `Ready`, then construct/run the `Game`** — identical on every platform this library supports. Desktop backends (`SkiaGlBackend`, `SkiaAngleBackend`, ...) complete `Ready` immediately since their GL/D3D11 context already exists by construction; `SkiaWebGlBackend.Ready` actually waits, because the host's WebGL2 context is created asynchronously by the browser (`host.Ready` under the hood — the backend forwards it).

```cs
var backend = new SkiaWebGlBackend(host);
await backend.Ready;
_game = new MyGame(host, backend); // your Game subclass; pass both through
_game.Run();
```

```cs
protected override void Initialize()
{
    SkiaRenderer.Initialize(_backend, GraphicsDevice); // _backend: passed into your constructor, already Ready
    base.Initialize();
}
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
