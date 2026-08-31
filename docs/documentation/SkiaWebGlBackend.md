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

## Members

| Member | Description |
| --- | --- |
| `Diagnostics` | Read-only counters and latest CPU timing for rendering, upload, resize, dropped frames, and context loss. |
| `GRContext` | The Skia GPU context owned by the host. It is available after the host has created its first surface. |
| `Initialize(GraphicsDevice)` | Attaches the backend to the KNI graphics device. `host.Ready` must already have completed. |
| `Dispose()` | Unsubscribes context events. Dispose your own `SkiaRenderTarget2D` instances first — this doesn't track or dispose them for you. |

## Options

`SkiaWebGlOptions` controls WebGL2 validation, direct `texSubImage2D` versus diagnostic `texImage2D`, Y orientation, premultiplied alpha, and color-space conversion. The production default path is direct canvas `texSubImage2D`; `DiagnosticTexImage2D` reallocates storage and is intended only for comparison.

## Example

```razor
<SkiaGameWebGlHost @ref="host" RequireWebGl2="true" />

@code {
    private SkiaGameWebGlHost? host;
    private SkiaRenderTarget2D? canvas;

    private async Task StartAsync(GraphicsDevice graphicsDevice)
    {
        await host!.Ready;

        var backend = new SkiaWebGlBackend(host, new SkiaWebGlOptions
        {
            RequireWebGl2 = true,
            FlipY = false,
            PremultiplyAlpha = true,
            DisableColorSpaceConversion = true,
        });

        SkiaRenderer.Initialize(backend, graphicsDevice);
        canvas = new SkiaRenderTarget2D(graphicsDevice, 480, 300);
    }

    private void DrawUi()
    {
        canvas!.Begin();
        canvas.Canvas.DrawCircle(240, 150, 100, paint);
        canvas.End();
    }
}
```

Call `canvas.Begin()`/`End()` only after ending any lower `SpriteBatch`. `canvas.Texture` is
current as soon as `End()` returns and can immediately be sampled by another `SpriteBatch`, a
`RenderTarget2D` pass, or a KNI effect.

## Remarks

- Rendering and upload are synchronous and must remain on the browser graphics thread.
- Only `SurfaceFormat.Color` / RGBA8 targets are supported.
- The normal upload path has no `readPixels`, managed pixel buffer, or per-frame `Texture2D` allocation.
- During `webglcontextlost`, pause calls to `canvas.Begin()`/`End()` until the host reports restoration.
- Dispose your `SkiaRenderTarget2D` instances, then `SkiaRenderer`, before disposing the host or replacing the backend/graphics device.

See also [WebGL quick start](../webgl/quickstart.md) and [troubleshooting](../webgl/troubleshooting.md).
