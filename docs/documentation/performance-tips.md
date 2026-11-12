# Performance tips

How to draw with SkiaGameRendering so it runs fast. What you draw and how you call `SKCanvas` decide most of
the frame time. The backend that renders it (OpenGL, Vulkan, Direct3D) and the wrapper around Skia decide very
little. In a profile of the MonoGame DesktopGL shapes scene, managed code (ours, SkiaSharp's and MonoGame's)
was under 1% of the frame. Skia's own native work was over 80%: 50% flushing work to the GPU and 31% recording
draw calls. So the levers are in your draw calls, and this page lists them.

The measurements of SkiaGameRendering against each engine's own drawing are in
[Performance measurements](../performance.md). This page uses those plus a set of Skia-only comparison scenes.

## How to read the numbers

- Every table names where it was measured. Results differ by platform and GPU, so check your own scene before
  relying on a number.
- Numbers marked **BRUTAL OpenGL** come from the BRUTAL OpenGL perf app on an RTX 5060 Laptop GPU with a
  Ryzen 7 260, one or two runs at 1600x900 into a `SkiaRenderTarget2D`. Where there were two runs, the table
  shows their average.
- Run-to-run noise on that machine is about ±20%: scenes that did not change moved that much between runs. A
  difference smaller than that is reported as "no clear difference", never as a finding.
- FPS covers the whole frame. Speedup is fast FPS divided by slow FPS.
- To reproduce a BRUTAL OpenGL row, run `Performance.Brutal.OGL.Skia` with `--page <page>` (for example
  `--page guide-shadows`) or `--scenes "<name>"`. See [Running](../performance.md#running).

## Summary

| Do this | Instead of | Speedup (BRUTAL OpenGL) |
|---|---|---:|
| Draw shadows with `SKMaskFilter.CreateBlur` | `SKImageFilter.CreateDropShadow` per item | 10x |
| Give an item transparency through the paint's alpha | `SaveLayer` per item | 6.6x |
| Draw an SVG as a pre-rasterized image | Replaying the SVG every frame | 5x to 67x |
| Use 4x MSAA or turn antialiasing off for many circles | Antialiased circles without MSAA | 1.4x to 2x |
| Cache `SKPath` objects | Rebuilding paths every frame | 1.6x |
| Group draws by type | Interleaving circles, rects, text and images | 1.6x |
| Draw shadows as a nine-slice image | `SKMaskFilter.CreateBlur` | 1.4x |
| Avoid a clip per item | `Save`, clip, draw, `Restore` per item | about 4x |
| Draw sprites with one `DrawAtlas` per texture | One `DrawImage` per sprite | many times (see [Sprites](#sprites-and-images)) |
| Draw many labels as one text blob | One `DrawText` per label | see [Text](#text) |

## Passes, render targets and backends

### Use few passes

Every `Begin()`/`End()` pair costs a fixed amount and flushes Skia. One screen-sized target per frame beats
one target per element. The fixed cost per pass, measured on an empty pass at 1920x1080 on the same machine:

| Backend | Begin + End per pass |
|---|---:|
| BRUTAL OpenGL | 0.05 ms |
| BRUTAL Vulkan | 0.1 ms |

On BRUTAL Vulkan, about 0.07 ms of that is the two image layout transitions. Passing `appManagesLayout: true`
to `SkiaRenderTarget2D` records them in your own command buffer instead and saves about 0.04 ms per pass.
The transitions do not stall the CPU behind earlier GPU work: a light pass submitted right after 1.5 ms of
GPU work still took 0.04 ms to begin and 0.08 ms to end.

### GL backends that switch contexts

On MonoGame and KNI DesktopGL, FNA OpenGL, Android, iOS, Godot's GL renderer and raylib, Skia draws on its
own GL context that shares textures with the engine's. Every pass switches to Skia's context and back. On
MonoGame DesktopGL (same machine, CPU profile), that switch cost about 0.3 ms per frame: 20-25% of the frame
with 500 circles and 2% with 10,000. It matters most in light scenes, and one pass per frame keeps it to one
switch pair. BRUTAL OpenGL shares the app's context and has no switch.

On WindowsDX and KNI WindowsDX, `SkiaRenderTarget2D.End()` also calls `glFinish`, which stalls the CPU until
the GPU finishes. `SkiaDirectCanvas.End()` does not.

### Prefer `SkiaDirectCanvas` where it is supported

It draws into the back buffer with no intermediate texture, so it skips the full-screen clear and composite of a
`SkiaRenderTarget2D`. An empty pass costs about 0.07-0.1 ms instead of 0.15-0.3 ms (MonoGame and KNI desktop
backends), and a scene that fills the screen gains the most. See [SkiaDirectCanvas](SkiaDirectCanvas.md) for
the platforms.

### Render target size

A bigger target costs more only when the scene is limited by filling pixels. With 2,000 shapes scaled to fill
the target, BRUTAL OpenGL ran at about the same speed at 1600x900 and at 3840x2160, because recording 2,000
draw calls was the limit. Screen-sized translucent layers are where size shows; see
[Fill rate](../performance/fill-rate.md).

| Target (2k shapes, BRUTAL OpenGL) | FPS |
|---|---:|
| 800x800 | 280 |
| 1600x900 | 212 |
| 3840x2160 | 217 |

## Antialiasing and MSAA

`SKPaint.IsAntialias` costs very different amounts depending on the shape. Each scene below draws one call per
item with no canvas transform, so the rows compare the shape alone (BRUTAL OpenGL, one run per column).

| Scene | AA on | AA off | 4x MSAA, AA on |
|---|---:|---:|---:|
| Circles 500 | 834 | 1201 | 1200 |
| Circles 10k | 62 | 107 | 122 |
| Rectangles 500 | 944 | 666 | 942 |
| Rectangles 10k | 76 | 65 | 90 |
| Rounded rectangles 500 | 1203 | 1114 | 1057 |
| Rounded rectangles 10k | 114 | 114 | 99 |

- **Circles:** antialiasing is most of their cost. 4x MSAA keeps smooth edges and runs as fast as AA off
  (1.4x at 500, 2x at 10k).
- **Rectangles and rounded rectangles:** no clear difference between AA on, AA off and MSAA. Skia's
  antialiasing for these shapes appears to be cheap; the reason is not measured.
- **Mixed shapes** (circles, stroked rounded rectangles, round-cap lines, triangle paths): turning AA off made
  them about 3x faster (2k: 41 to 126 FPS; 10k: 8 to 25 FPS).
- **Sprites, images and text:** no clear difference with AA off. Antialiasing a textured quad seems to be a few
  shader instructions on an already batched draw; that explanation is not measured.
- **UI panels** (shadow, panel, border, icon, label) ran 24-32% slower with AA off on BRUTAL OpenGL and
  10-14% slower on BRUTAL Vulkan, one run each. The cause is not known.

To turn on MSAA, pass `multiSampleCount` to `SkiaRenderTarget2D`
(`SkiaRenderer.SupportsMultiSampledTargets ? 4 : 0` is portable on the MonoGame-family backends; it is false
until the renderer is initialized), or set `PreferMultiSampling` on MonoGame's `GraphicsDeviceManager` for the
back buffer. BRUTAL takes `multiSampleCount` on both backends. On the MonoGame desktop backends, scenes full of
shadows or already-rasterized images got slower with MSAA, so measure your own scene; see
[Multisampling](SkiaRenderTarget2D.md#multisampling).

## Draw order and batching

Skia batches consecutive draws that it can combine. Switching between kinds of draw (shape, text, image)
breaks the batch.

| Draw order (2k items: circle, rect, text, image; BRUTAL OpenGL) | Slow FPS | Fast FPS | Speedup |
|---|---:|---:|---:|
| Interleaved by item vs grouped by type | 122 | 191 | 1.6x |

Real UI usually draws each card completely (shadow, panel, border, icon, label) before the next, which
interleaves types. Drawing the UI scene card by card instead of all panels, then all icons, then all labels cost
27% at 50 cards and 45% at 1,000 cards on BRUTAL OpenGL (608 to 445 FPS and 53 to 29 FPS, one run each).
When cards do not overlap, drawing all panels first, then all icons, then all labels gives the same picture
faster.

## Transparency and layers

| Per-item transparency (250 items, BRUTAL OpenGL) | Slow FPS | Fast FPS | Speedup |
|---|---:|---:|---:|
| `SaveLayer` per item vs alpha in the paint's color | 108 | 717 | 6.6x |

A `SaveLayer` allocates an offscreen layer and composites it back, so one per item is expensive. Put the alpha in
`SKPaint.Color` when the item is one draw. One `SaveLayer` around the whole scene showed no clear cost (296 FPS
without, 292 with, 2,000 shapes), so a layer for a whole group is fine.

## Shadows

| Shadows (100 UI-sized panels, BRUTAL OpenGL) | Slow FPS | Fast FPS | Speedup |
|---|---:|---:|---:|
| `SKImageFilter.CreateDropShadow` vs `SKMaskFilter.CreateBlur` | 65 | 652 | 10x |
| `SKMaskFilter.CreateBlur` vs nine-slice shadow image | 652 | 899 | 1.4x |

- Never give each item its own `CreateDropShadow` image filter. Draw the shadow as a blurred shape with
  `SKMaskFilter.CreateBlur`, then draw the item over it.
- Fastest is a pre-rendered shadow image stretched with `DrawImageLattice` (nine-slice), so each shadow is a
  textured draw.
- Blur cost depends on the panel size. In an early quick run, 500 small panels (25-50 px tall) fell to about
  7 FPS while UI-sized panels ran at about 180 FPS; this was not repeated, so treat it as a warning, not a
  number.
- Blurred shadows are the widest gap between Skia and the engines' own drawing; see
  [Drop shadows](../performance/shadows.md).

## Paths, paints and fonts: reuse objects

| Reuse (BRUTAL OpenGL) | Slow FPS | Fast FPS | Speedup |
|---|---:|---:|---:|
| `SKPath` rebuilt every frame vs cached (1k paths) | 224 | 365 | 1.6x |
| New `SKPaint` per draw vs one reused paint (2k draws) | 252 | 314 | 1.25x |
| New `SKFont` per label vs a cached font (500 labels) | 247 | 259 | no clear difference |

- Build an `SKPath` once and keep it. Skia caches GPU work per path, so a path rebuilt every frame pays that
  work again (cause from Skia's design, not measured here).
- Reuse one `SKPaint` and change its color between draws. A new paint per draw was slower in both runs, though
  the gap is close to the noise band.
- Creating `SKFont` objects was not measurably slower, but caching them costs nothing either.

## Text

- **Draw many labels as one text blob.** Build one `SKTextBlobBuilder` run per frame and issue a single
  `DrawText(blob, 0, 0, paint)` instead of one `DrawText` per label. A blob has one paint, so batch per color.
  The output is pixel-identical. See [Text, one font](../performance/text.md#faster-batch-labels-into-one-text-blob).
- `SKFont` settings made no measurable difference on MonoGame DesktopGL: `Edging` (`Alias`, `Antialias`),
  `Subpixel`, `Hinting`, `LinearMetrics`, `BaselineSnap` (7 runs; see [Text](../performance/text.md)).
- Many font sizes cost more than one; see [Text, mixed fonts](../performance/text-mixed.md).

## Sprites and images

- **Draw sprites with `SKCanvas.DrawAtlas`**, one call per texture sheet, with one `SKRotationScaleMatrix` per
  sprite. Upload the sheet once with `SKImage.ToTextureImage(SkiaRenderer.CurrentBackend.GRContext)`, called
  between `Begin()` and `End()`, and draw from that image. Tint through `DrawAtlas`'s `colors` array with
  `SKBlendMode.Modulate`, the equivalent of `SpriteBatch.Draw`'s `color`; `SKColor` is unpremultiplied, so the
  matching `SpriteBatch` color is `Color.FromNonPremultiplied(r, g, b, a)`. One call per sprite (`DrawImage`,
  `DrawImageRect`) is many times slower; see
  [Sprites, one texture](../performance/sprites-single.md#faster-one-drawatlas-not-one-call-per-sprite).
- Sampling mode is a choice of look. With 10,000 downscaled sprites on BRUTAL OpenGL, linear ran at 831 FPS,
  nearest at 796 and linear with mipmaps at 759, all within the noise band.

## Strokes

- **Set `SKPaint.StrokeJoin = SKStrokeJoin.Round` on rounded-rectangle borders.** The default miter join is the
  slow part of a stroked `DrawRoundRect`. The edges are not pixel-identical to the default. See
  [Shapes](../performance/shapes.md#stroked-rounded-rectangles).
- Stroke width made no clear difference for 2,000 circles and rounded rectangles on BRUTAL OpenGL: fill
  277 FPS, 1 px stroke 289, hairline (width 0) 283, 4 px stroke 264.

## Clipping

| Clipping (2k circles, BRUTAL OpenGL) | Slow FPS | Fast FPS | Speedup |
|---|---:|---:|---:|
| `Save`, `ClipRect`, draw, `Restore` per item vs no clip | 86 | 357 | about 4x |
| Same with `ClipRoundRect` | 95 | 357 | about 4x |
| Same with `ClipPath` | 86 | 357 | about 4x |

A clip per item costs about 4x, and the clip's shape barely matters. The two runs disagreed by about 20%, so
the exact factor is rough. Clip once around a group, or draw shapes that need no clip, such as a rounded
rectangle instead of a rectangle clipped to a rounded rectangle.

## Static content

| Static content (2k shapes, BRUTAL OpenGL) | Slow FPS | Fast FPS | Speedup |
|---|---:|---:|---:|
| Drawing every frame vs replaying a recorded `SKPicture` | 197 | 225 | no clear difference |

Replaying an `SKPicture` still sends every draw to the GPU each frame, so it saves only the managed calls. It
was faster in both runs, by less than the noise band. Content that does not change is better drawn once into
its own `SkiaRenderTarget2D` and then drawn as a texture each frame (not measured).

## Alternatives

When Skia's drawing of something is slow, these replace it with something cheaper:

| Instead of | Use | Where measured |
|---|---|---|
| A blurred shadow per item | A nine-slice shadow image (`DrawImageLattice`) | 1.4x faster, BRUTAL OpenGL |
| Replaying an SVG every frame | The SVG rasterized once to an image | 5x at 100 icons (307 to 1596 FPS) and 67x at 2,000 (19 to 1282 FPS), BRUTAL OpenGL; see [SVG](../performance/svg.md) |
| One `DrawImage` per sprite | `DrawAtlas` per texture | [Sprites, one texture](../performance/sprites-single.md) |
| One `DrawText` per label | One text blob per color | [Text, one font](../performance/text.md) |
| Antialiased circles | 4x MSAA | 1.4x to 2x, BRUTAL OpenGL |
| Content that never changes | Draw it once into a `SkiaRenderTarget2D`, then draw that texture | not measured |
| Thousands of strokes, round caps or blurred shadows per frame on the desktop | A shader-based library such as [Apos.Shapes](https://github.com/Apostolique/Apos.Shapes) next to Skia | [Shapes](../performance/shapes.md), [Drop shadows](../performance/shadows.md) |

## What doesn't matter

These made no measurable difference, so pick whatever reads best:

- **Managed overhead.** Our code, SkiaSharp's interop and MonoGame's managed code were under 1% of the frame
  in a MonoGame DesktopGL profile. Skia's native work is the cost.
- **SkiaSharp 4.153 against 4.156.** The release notes for 4.154 to 4.156 and Skia milestones 154 to 156 list
  no performance changes (not benchmarked).
- **The wrapper on BRUTAL.** 0.05 ms per pass on OpenGL and 0.1 ms on Vulkan.
- **Stroke width,** for plain circles and rounded rectangles.
- **Sampling mode** (nearest, linear, mipmaps) on downscaled sprites.
- **One `SaveLayer` around a whole scene.**
- **Creating `SKFont` objects,** and `SKFont` rendering settings.
- **Antialiasing on sprites, images, text, rectangles and rounded rectangles.**

## What to expect

Skia gives every engine one drawing API with vector shapes, text at any size with no atlas to bake, and
effects the engines lack. Some scenes cost more frame time than the engine's own drawing, and the scene pages
show which. The tips above close most of that gap.

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
  shader. For thousands of them per frame, use the matching tips above: 4x MSAA, cached paths, round stroke
  joins and nine-slice shadows. See [SVG](../performance/svg.md) and [Drop shadows](../performance/shadows.md).
