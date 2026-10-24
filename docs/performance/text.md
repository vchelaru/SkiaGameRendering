# Text, one font

Labels of about 24 characters in one 20 px font, from Noto Sans.

- **Engine side:** A `SpriteFont` baked at run time from the TTF (a fixed-size glyph atlas, as the content pipeline would build), and FontStashSharp (dynamic sizes, glyphs rasterized on demand).
- **Skia side:** `SKFont` and `SKCanvas.DrawText`, one call per label.

| Engine | Skia (direct) |
|---|---|
| ![Text, one font, engine](images/text-engine.jpg) | ![Text, one font, Skia](images/text-skia.jpg) |

Screenshots: 200 labels, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Text 50 | 1679 (SpriteFont) | 1026 | -39% |
| Text 200 | 1636 (FontStashSharp) | 673 | -59% |
| Text 1k | 1093 (SpriteFont) | 199 | -82% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Text 50 | 2641 (FontStashSharp) | 1053 | -60% |
| Text 200 | 2295 (SpriteFont) | 747 | -67% |
| Text 1k | 1383 (SpriteFont) | 190 | -86% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Text 50 | 2112 (FontStashSharp) | 1082 | -49% |
| Text 200 | 1520 (SpriteFont) | 611 | -60% |
| Text 1k | 509 (SpriteFont) | 162 | -68% |

<!-- results:end -->

## Reading the numbers

- Skia is 39-86% slower than the faster of `SpriteFont` and FontStashSharp on the desktop, and 49-68% slower in the browser. The gap grows with the label count.
- Skia draws text at any size with no atlas to bake; see the mixed-fonts page.
