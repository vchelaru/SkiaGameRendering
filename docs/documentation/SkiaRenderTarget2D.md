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
`SkiaGameRendering.WindowsDX`, `SkiaGameRendering.Android`, `SkiaGameRendering.iOS`, `SkiaGameRendering.Kni.DesktopGL`,
`SkiaGameRendering.Kni.WindowsDX`, `SkiaGameRendering.Kni.Android`, `SkiaGameRendering.Kni.iOS`, `SkiaGameRendering.Kni.WebGL`)

```csharp
public sealed class SkiaRenderTarget2D : IDisposable
```

## Constructor

| Signature | Description |
| --- | --- |
| `SkiaRenderTarget2D(GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType = SKColorType.Rgba8888, int multiSampleCount = 0)` | Auto-initializes `SkiaRenderer` on first use and allocates a fixed-size surface. `multiSampleCount` works like `RenderTarget2D`'s `preferredMultiSampleCount`; see [Multisampling](#multisampling). |

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

## Multisampling

`multiSampleCount` is 0 by default, which draws exactly as before. Pass 2, 4 or 8 to back the target with a
multisampled surface: the GPU smooths edges itself, so Skia can use faster paths for shapes and strokes and
spends less CPU recording them. Skia resolves the samples into `Texture` on the GPU at `End()`, with no readback.

```csharp
var canvas = new SkiaRenderTarget2D(GraphicsDevice, 1600, 900, multiSampleCount: 4);
```

Any other value, a count the GPU does not offer, or a backend without support throws `NotSupportedException`.
It never falls back to 0.

**Supported on** the OpenGL backends: MonoGame DesktopGL, Android and iOS, KNI DesktopGL, Android and iOS, and
FNA OpenGL. **Throws `NotSupportedException`** on MonoGame WindowsDX (ANGLE/D3D11), WindowsDX12, DesktopVK, KNI
WindowsDX, KNI WebGL, FNA WindowsDX and FNA WebGL. Godot, raylib, Stride and Unity have their own target types
and do not offer it yet.

**Measured**, on a Ryzen 7 260 with an RTX 5060 Laptop GPU (driver 32.0.16.1047), MonoGame DesktopGL, 1600x900,
4 samples against none. CPU time of the render-target draw, median of 10 interleaved runs (a ratio above 1x is faster):

| scene | 4 samples vs none |
|---|---|
| Circles, 500 / 2k / 10k | 1.40x / 1.77x / 1.92x |
| Mixed shapes, 2k / 10k | 2.36x / 2.23x |
| SVG, 100 / 500 / 2k | 1.25x / 1.60x / 1.41x |
| Shadows, 100 / 500 / 2k | 0.91x / 0.91x / 0.96x (slower) |
| Pre-rasterized SVG, 100 / 500 / 2k | 0.93x / 0.89x / 0.86x (slower) |

It costs about 14 MiB of extra GPU memory at 1600x900 (whole-GPU reading, median of 3 runs), and the resolve
adds work to every `End()`. Edges differ slightly from the unsampled result: on the mixed shapes 2k scene,
118,889 of 686,054 non-black pixels differ, by at most 121 levels in a channel. Interior pixels are identical.

**Leave it off** when the target mostly draws shadows or already-rasterized images (a pre-rasterized SVG
drawn as a picture): those got slower. Leave it off too where memory is tight, or where pixel-exact output
matters, such as comparing against a golden image. Measure your own scene before turning it on.

`SkiaDirectCanvas` has no such option. Its equivalent is enabling MSAA on the engine's back buffer (for
example `PreferMultiSampling` on MonoGame's `GraphicsDeviceManager`); Skia follows the sample count automatically.

## Remarks

- `End()`'s composite always uses `SpriteSortMode.Deferred` with default blend/sampler/effect
  state, drawn at the surface's native size and the origin (`Vector2.Zero`) of whatever render
  target is currently bound. Set a render target before calling `Begin()` if you want it to land
  somewhere other than the back buffer.
- Size is fixed for the object's lifetime, like `RenderTarget2D` — construct a new
  `SkiaRenderTarget2D` (and `Dispose()` the old one) for a different size.
- Tear the shared backend down (e.g. on exit, or before switching backends) with
  `SkiaRenderer.Dispose()`. Dispose your own `SkiaRenderTarget2D` instances first.
- See the sample this pattern is drawn from: `samples/Sample.Gum.Kni.WebGL/Gum/SkiaGumRenderable.cs`
  and `samples/Sample.Gum.Kni.WebGL/Game1.cs`.
