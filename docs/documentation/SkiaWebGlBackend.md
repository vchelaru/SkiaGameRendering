# SkiaWebGlBackend

## Definition

`SkiaWebGlBackend` is the browser-specific `SkiaBackend` that drives a `SkiaRenderTarget2D`'s
render pass into the hidden GPU canvas owned by `SkiaGameWebGlHost`, flushes Skia, and uploads
that canvas into the target's KNI `Texture2D`.

Namespace: `SkiaGameRendering.Kni.WebGL`

Assembly/package: `SkiaGameRendering.Kni.WebGL`

```csharp
[SupportedOSPlatform("browser")]
public sealed class SkiaWebGlBackend : SkiaBackend
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaWebGlBackend(SkiaGameWebGlHost host, SkiaWebGlOptions? options = null)` | Creates a backend for a ready host. The backend subscribes to the host's context lifecycle until disposed. |

Most code never calls this constructor directly — `SkiaRenderer.AttachHost(host, options)` does it
for you, once, from the page's code-behind (see [Example](#example) below). Construct it explicitly
only if your `Game` can receive a pre-built backend through its own constructor and you have a
specific reason to skip the ambient path.

## Members

| Member | Description |
| --- | --- |
| `Diagnostics` | Read-only counters and latest CPU timing for rendering, upload, resize, dropped frames, and context loss. |
| `Options` | The `SkiaWebGlOptions` this backend was constructed with — mutate it live (e.g. to toggle `UploadMode`) without reconstructing the backend. |
| `GRContext` | The Skia GPU context owned by the host. It is available after the host has created its first surface. |
| `Ready` | Overrides `SkiaBackend.Ready`; forwards to the host's `Ready` (resolves once the browser has created the WebGL2 context). Must complete before `Initialize` is called. |
| `Initialize(GraphicsDevice)` | Attaches the backend to the KNI graphics device. `Ready` must already have completed. |
| `Dispose()` | Unsubscribes context events. Dispose your own `SkiaRenderTarget2D` instances first — this doesn't track or dispose them for you. |

## SkiaRenderer's web extension

This package adds a web-only partial extension to `SkiaRenderer` (`SkiaRenderer.Web.cs`, compiled
only into `SkiaGameRendering.Kni.WebGL`):

| Member | Description |
| --- | --- |
| `SkiaRenderer.AttachHost(SkiaGameWebGlHost host, SkiaWebGlOptions? options = null)` | Attaches the page's host so `SkiaRenderer.Initialize(GraphicsDevice)` can build a `SkiaWebGlBackend` from it once ready. Call once, page-lifetime, right after the host mounts — never from `Game` code. |

Once attached, `SkiaRenderer.IsReady` and `SkiaRenderer.Initialize(GraphicsDevice)` (both declared
on `SkiaRenderer`'s shared, platform-agnostic part — see `docs/documentation/SkiaRenderTarget2D.md`
and the desktop docs) do the rest. No other WebGL-specific member needs to appear in `Game` code.

## Options

`SkiaWebGlOptions` controls WebGL2 validation, direct `texSubImage2D` versus diagnostic `texImage2D`, Y orientation, premultiplied alpha, and color-space conversion. The production default path is direct canvas `texSubImage2D`; `DiagnosticTexImage2D` reallocates storage and is intended only for comparison.

## Example

```razor
<SkiaGameWebGlHost @ref="host" RequireWebGl2="true" />

@code {
    private SkiaGameWebGlHost? host;
    private MyGame? game;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        SkiaRenderer.AttachHost(host!); // once, page-lifetime - the only place a WebGL type is named
        await host!.Ready;              // optional - only if the page itself wants to know when ready
        game = new MyGame();
        game.Run();
    }
}
```

`MyGame` (a `Microsoft.Xna.Framework.Game` subclass) never references `SkiaGameWebGlHost` or
`SkiaWebGlBackend` at all:

```csharp
protected override void Draw(GameTime gameTime)
{
    if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
        SkiaRenderer.Initialize(GraphicsDevice);

    if (SkiaRenderer.IsInitialized)
    {
        canvas ??= new SkiaRenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        canvas.Begin();
        canvas.Canvas.DrawCircle(240, 150, 100, paint);
        canvas.End();
    }
    base.Draw(gameTime);
}
```

This is deliberate: `IsReady`/`Initialize(GraphicsDevice)` are declared on `SkiaRenderer`'s shared,
platform-agnostic part, so the exact same `Game` code also compiles and behaves correctly on
desktop (`IsReady` is always `true` there). It's also the only pattern that works for a host that
constructs `Game` itself with no constructor hook (e.g. `Activator.CreateInstance` — an in-browser
code fiddle). Need backend-specific access (diagnostics, live option mutation)?
`SkiaRenderer.CurrentBackend as SkiaWebGlBackend` gets you there once `IsInitialized` is true.

Call `canvas.Begin()`/`End()` only after ending any lower `SpriteBatch`. `canvas.Texture` is
current as soon as `End()` returns and can immediately be sampled by another `SpriteBatch`, a
`RenderTarget2D` pass, or a KNI effect.

## Remarks

- Rendering and upload are synchronous and must remain on the browser graphics thread.
- Only `SurfaceFormat.Color` / RGBA8 targets are supported.
- The normal upload path has no `readPixels`, managed pixel buffer, or per-frame `Texture2D` allocation.
- During `webglcontextlost`, pause calls to `canvas.Begin()`/`End()` until the host reports restoration.
- Dispose your `SkiaRenderTarget2D` instances, then `SkiaRenderer`, before disposing the host or replacing the backend/graphics device.
- `canvas.End()` composites the whole surface at its native size and the origin. If you need to
  draw the result more than once, at a different size, or sample it in a shader (as the WebGL
  sample's Gum panel does — see `samples/Sample.Kni.WebGL/Gum/SkiaGumRenderable.cs`), call
  `canvas.EndWithoutDrawing()` instead and composite `canvas.Texture` yourself. See
  [SkiaRenderTarget2D](SkiaRenderTarget2D.md).

See also [WebGL quick start](../webgl/quickstart.md) and [troubleshooting](../webgl/troubleshooting.md).
