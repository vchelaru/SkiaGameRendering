# SkiaRenderTarget2D

## Definition

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into. It works like
`SpriteBatch`'s own Begin/End shape: individual shapes carry their own position (via Skia's
drawing API, the same way `SpriteBatch.Draw` carries its own), and `End()` composites the whole
surface onto whatever render target is currently bound — no separate
`spriteBatch.Draw(canvas.Texture, ...)` step, the same way `SpriteBatch.End()` needs no separate
step to show its queued sprite draws. Construct it sized to match whatever you intend to draw it
onto — typically the back buffer, or a `RenderTarget2D` the same size as the viewport.

Namespace: `SkiaGameRendering`

Assembly/package: `SkiaGameRendering` (and the platform packages that reference it —
`SkiaGameRendering.WindowsDX`, `SkiaGameRendering.Kni.DesktopGL`,
`SkiaGameRendering.Kni.WindowsDX`, `SkiaGameRendering.Kni.WebGL`)

```csharp
public sealed class SkiaRenderTarget2D : IDisposable
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaRenderTarget2D(GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888)` | Auto-initializes `SkiaRenderer` on first use and allocates a fixed-size surface. |

## Members

| Member | Description |
| --- | --- |
| `Texture` | The underlying `Texture2D`. Mainly useful alongside `EndWithoutDrawing`. |
| `Canvas` | The `SKCanvas` to draw on. Only valid between `Begin` and `End`/`EndWithoutDrawing`. |
| `Begin(bool clear = true)` | Begins a render pass; throws if a previous `Begin` hasn't been closed yet. |
| `End()` | Ends the render pass and composites the whole surface, at native size and the origin, onto whatever's currently bound. |
| `EndWithoutDrawing()` | Same as `End`, but skips the composite. |
| `Dispose()` | Releases the GPU resources. Throws if called between `Begin` and `End`. |

## Example

The common case — one canvas, drawn once, matching its target's size:

```csharp
var canvas = new SkiaRenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

canvas.Begin();
canvas.Canvas.DrawCircle(100, 100, 100, paint);
canvas.End();
```

Many shapes, one shared canvas — the same pattern `SpriteBatch` uses for many sprites in one pass:

```csharp
canvas.Begin();
foreach (var entity in entities)
    canvas.Canvas.DrawCircle(entity.X, entity.Y, entity.Radius, entity.Paint);
canvas.End();
```

Reusing the result more than once (a nested `RenderTarget2D` pass, a second draw at a different
size, and shader sampling) needs the raw texture, so skip the composite:

```csharp
canvas.Begin();
DrawContent(canvas.Canvas);
canvas.EndWithoutDrawing();

spriteBatch.Draw(canvas.Texture, new Rectangle(40, 100, 394, 246), Color.White);

GraphicsDevice.SetRenderTarget(uiTarget);
spriteBatch.Begin();
spriteBatch.Draw(canvas.Texture, Vector2.Zero, Color.White);
spriteBatch.End();
GraphicsDevice.SetRenderTarget(null);

shader.Texture = canvas.Texture;
// ... draw with the shader
```

## Remarks

- `End()`'s composite always uses `SpriteSortMode.Deferred` with default blend/sampler/effect
  state, drawn at the surface's native size and the origin (`Vector2.Zero`) of whatever render
  target is currently bound. Set a render target before calling `Begin()` if you want it to land
  somewhere other than the back buffer.
- Size is fixed for the object's lifetime, like `RenderTarget2D` — construct a new
  `SkiaRenderTarget2D` (and `Dispose()` the old one) for a different size.
- Tear the shared backend down (e.g. on exit, or before switching backends) with
  `SkiaRenderer.Dispose()`. Dispose your own `SkiaRenderTarget2D` instances first.
- See the sample this pattern is drawn from: `samples/Sample.Kni.WebGL/Gum/SkiaGumRenderable.cs`
  and `samples/Sample.Kni.WebGL/Game1.cs`.
