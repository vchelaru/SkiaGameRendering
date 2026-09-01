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

Render `<SkiaGameWebGlHost @ref="host" />` on the page. Then, once, right after it mounts, attach it
to `SkiaRenderer` — this is the *only* place a WebGL-specific type gets named anywhere in this
pattern:

```cs
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (!firstRender)
        return;

    SkiaRenderer.AttachHost(host);
    await host.Ready; // optional - only needed if the page itself wants to know when ready
    _game = new MyGame();
    _game.Run();
}
```

Your `Game` subclass never touches `SkiaGameWebGlHost`/`SkiaWebGlBackend`, never takes constructor
arguments, and never awaits anything. It just polls `SkiaRenderer.IsReady` from `Draw()` (a
rendering-setup concern, colocated with the rendering that follows it) before calling
`SkiaRenderer.Initialize(GraphicsDevice)`:

```cs
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

`IsReady` and `Initialize(GraphicsDevice)` are declared on `SkiaRenderer`'s shared, platform-agnostic
part — the same one every other backend uses — so this exact code also compiles and behaves
correctly on desktop, where `IsReady` is always `true` (nothing to wait for) and `Initialize`
auto-detects the right backend via reflection. That's deliberate: it means the same `Game` code
works whether it's running on web or gets exported into a real desktop project unchanged. Need
backend-specific access (diagnostics, options)? `SkiaRenderer.CurrentBackend as SkiaWebGlBackend`
gets you there once `IsInitialized` is true — see `samples/Sample.Kni.WebGL/Game1.cs`.

Only reach for explicit construction (`new SkiaWebGlBackend(host)` +
`SkiaRenderer.Initialize(backend, graphicsDevice)`) if your host *can* pass a backend into `Game`'s
constructor and you have a specific reason to bypass the ambient path — `AttachHost` covers the
normal case, including hosts that construct `Game` themselves with no constructor hook (e.g. an
in-browser code fiddle).

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
