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

## Drawing straight into KNI's canvas

`SkiaDirectCanvas` draws into whatever KNI is rendering to when `Begin()` is called, the canvas's own
back buffer or the bound `RenderTarget2D`, with no second canvas and no upload. It needs the same host
attached as above, and has the same `Begin`/`Canvas`/`End` shape as `SkiaRenderTarget2D`:

```cs
_skia = new SkiaDirectCanvas(GraphicsDevice); // once

spriteBatch.Begin(); /* background */ spriteBatch.End();
_skia.Begin();
_skia.Canvas.DrawCircle(100, 100, 50, paint);
_skia.End();
spriteBatch.Begin(); /* foreground */ spriteBatch.End();
```

`Begin()` does not clear, and coordinates are target pixels from the top left; KNI's `Viewport` and
scissor rectangle are ignored. A multisampled back buffer or render target works. Only one bound
`SurfaceFormat.Color` `RenderTarget2D` is supported (not a cube map, not several at once). Use it
instead of `SkiaRenderTarget2D` where the upload is the problem: on Firefox, `SkiaRenderTarget2D`'s upload costs
32 ms or more a frame at 1080p, and `SkiaDirectCanvas` has none (see `validated-baseline.md`).

How it works, and what to know:

- WebGL cannot share objects between contexts, so Skia and KNI drive the one context. KNI's
  `WebGL2RenderingContext` is registered with SkiaSharp's Emscripten GL runtime on the first `Begin()`,
  and a bound `RenderTarget2D`'s framebuffer is registered with it the same way.
- Skia leaves GL state behind that KNI's cache does not know about. Each pass saves the bindings KNI
  relies on and restores them in `End()`, then resets KNI's cache by reflection (`KniStateInvalidation`),
  so KNI's next draw re-applies everything. You do nothing extra.
- A pass costs a few GL calls and one reset of KNI's state, on top of Skia's own work. Use few, large
  passes rather than one per element.
- KNI does not recover from a lost WebGL context (its buffers, programs and textures are gone). While it
  is lost, `Begin()` throws `InvalidOperationException`; catch it and skip the frame. When the context
  comes back, Skia's context and surfaces are rebuilt on the next `Begin()` and
  `SkiaWebGlBackend.Diagnostics.DirectContextLossCount` goes up.
- Skia's surface follows the canvas's drawing buffer, so a canvas resize needs nothing from you. KNI's
  BlazorGL platform cannot resize its own back buffer, though (see the sample's `Viewport` handling).
- It works alongside `SkiaRenderTarget2D` and the host's hidden canvas; losing that canvas does not affect it.
- In a trimmed or AOT build the reflected KNI members are kept by `[DynamicDependency]`;
  `tests/Tests.Kni.WebGL.Reflection` fails on a KNI bump that renames one.

`samples/Sample.Kni.WebGL` has a draw-mode selector (or `?mode=direct`) that runs it between SpriteBatch
draws on the back buffer, a render target and a 4x multisampled one; `?mode=scene` draws only the Scene
through it.

## Build and run the sample in this repo

If you're working from a clone of this repo rather than a fresh project:

```powershell
dotnet workload install wasm-tools-net8
dotnet build samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release
dotnet run --project samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release --no-build
```

That sample draws the shared scene every other sample draws. `samples\Sample.Gum.Kni.WebGL` (same commands) covers SpriteBatch interleaving, render-target consumption, shader sampling, animated Gum content, pointer/touch/wheel/text input, fractional DPR handling, and backend recreation.

## Packages

`SkiaGameRendering.Kni.WebGL` consumes stock KNI from NuGet (`nkast.Kni.Platform.Blazor.GL`) — no fork or source patch. Getting the destination `WebGLTexture`/rendering-context handles KNI doesn't expose publicly (see `WebGlCanvasUpload.cs`'s `KniWebGlInternals`), and resetting KNI's state cache for `SkiaDirectCanvas` (`KniStateInvalidation.cs`), is done via reflection into KNI internals instead.
