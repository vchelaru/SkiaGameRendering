# Performance tips

How to draw with SkiaGameRendering so it runs fast. Each tip links to the page that measures it. The numbers
come from [Performance measurements](../performance.md); check your own scene before relying on them.

## What to do first

- **Prefer `SkiaDirectCanvas` where it is supported.** It draws into the back buffer with no intermediate
  texture, so it skips the full-screen clear and composite of a `SkiaRenderTarget2D`: an empty pass costs
  about 0.07-0.1 ms instead of 0.15-0.3 ms, and a scene that fills the screen gains the most. See
  [SkiaDirectCanvas](SkiaDirectCanvas.md) for the platforms.
- **Turn on MSAA on the engine back buffer** (for example `PreferMultiSampling` on MonoGame's
  `GraphicsDeviceManager`). Skia follows the sample count and can use faster paths for shapes and strokes. If you
  draw into a `SkiaRenderTarget2D` on an OpenGL backend, pass `multiSampleCount` (`SkiaRenderer.SupportsMultiSampledTargets ? 4 : 0` is portable; it is false until the renderer is initialized). Scenes full of shadows or
  already-rasterized images got slower with it, so measure first; see
  [Multisampling](SkiaRenderTarget2D.md#multisampling).
- **Use few, large targets.** Every `Begin()`/`End()` pair switches GPU context and flushes Skia. On
  WindowsDX and KNI WindowsDX, `SkiaRenderTarget2D.End()` also calls `glFinish`, which stalls the CPU until the
  GPU finishes; `SkiaDirectCanvas.End()` does not. One screen-sized target per frame beats one per element.
- **Draw sprites with `SKCanvas.DrawAtlas`**, one call per texture sheet, with one `SKRotationScaleMatrix`
  per sprite. Upload the sheet once with `SKImage.ToTextureImage(SkiaRenderer.CurrentBackend.GRContext)`,
  called between `Begin()` and `End()`, and draw from that image. Tint through `DrawAtlas`'s `colors` array
  with `SKBlendMode.Modulate`, the equivalent of `SpriteBatch.Draw`'s `color`; `SKColor` is unpremultiplied, so
  the matching `SpriteBatch` color is `Color.FromNonPremultiplied(r, g, b, a)`. One call per sprite
  (`DrawImage`, `DrawImageRect`) is many times slower; see
  [Sprites, one texture](../performance/sprites-single.md#faster-one-drawatlas-not-one-call-per-sprite).
- **Draw many labels as one text blob.** Build one `SKTextBlobBuilder` run per frame and issue a single
  `DrawText(blob, 0, 0, paint)` instead of one `DrawText` per label. A blob has one paint, so batch per color.
  The output is pixel-identical. See [Text, one font](../performance/text.md#faster-batch-labels-into-one-text-blob).
- **Set `SKPaint.StrokeJoin = SKStrokeJoin.Round` on rounded-rectangle borders.** The default miter join is the
  slow part of a stroked `DrawRoundRect`. The edges are not pixel-identical to the default. See
  [Shapes](../performance/shapes.md#stroked-rounded-rectangles).

## What to expect

Skia is not faster than the engine at everything, and the scene pages show where.

- **Close to native, or ahead:** sprites through `DrawAtlas`, and full UI panels (shadow, border, icon and
  text in one pass), where Skia draws everything in one pass and the native side needs several libraries
  ([UI panels](../performance/ui.md)). In the browser Skia is often ahead, because the managed engine code
  runs as WebAssembly.
- **Behind native on the desktop:** plain shapes, gradients, text and screen-sized layers. See
  [Shapes](../performance/shapes.md), [Gradients](../performance/gradients.md),
  [Text, one font](../performance/text.md), [Text, mixed fonts](../performance/text-mixed.md) and
  [Fill rate](../performance/fill-rate.md).
- **Furthest behind:** strokes, round caps and paths (the mixed shapes scene), a replayed SVG, and blurred
  shadows from `SKMaskFilter.CreateBlur`. Skia tessellates these paths, where the native side uses a closed-form
  shader; the shadow gap is the widest in the suite. For thousands of them per frame on the desktop, a shader-based library is
  the faster choice. See [SVG](../performance/svg.md) and [Drop shadows](../performance/shadows.md).
- **What Skia is for:** vector content, text at any size with no atlas to bake, and one drawing API on
  every engine.
