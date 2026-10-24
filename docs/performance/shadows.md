# Drop shadows

Panels with a blurred drop shadow (offset 4, 6; blur 12), the shape UI toolkits draw behind buttons and dialogs. The scenes use five panel sizes, as a UI does; the last scene gives every panel its own size.

- **Engine side:** `Apos.Shapes` `FillRectangleBlurred` for the shadow, then `FillRectangle` for the panel. Blur is computed in the shader, with no extra pass.
- **Skia side:** `SKMaskFilter.CreateBlur` on `DrawRoundRect` for the shadow, then the panel.

| Engine | Skia (direct) |
|---|---|
| ![Drop shadows, engine](images/shadows-engine.jpg) | ![Drop shadows, Skia](images/shadows-skia.jpg) |

Screenshots: 500 shadows, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Shadows 100 | 758 (Apos.Shapes) | 116 | -85% |
| Shadows 500 | 216 (Apos.Shapes) | 28 | -87% |
| Shadows 2k | 59 (Apos.Shapes) | 6 | -90% |
| Shadows 500 unique sizes | 206 (Apos.Shapes) | 27 | -87% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Shadows 100 | 890 (Apos.Shapes) | 106 | -88% |
| Shadows 500 | 239 (Apos.Shapes) | 28 | -88% |
| Shadows 2k | 64 (Apos.Shapes) | 6 | -91% |
| Shadows 500 unique sizes | 243 (Apos.Shapes) | 27 | -89% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Shadows 100 | 83 (Apos.Shapes) | 22 | -73% |
| Shadows 500 | 27 (Apos.Shapes) | 3 | -89% |
| Shadows 2k | 4 (Apos.Shapes) | 1 | -75% |
| Shadows 500 unique sizes | 17 (Apos.Shapes) | 2 | -88% |

<!-- results:end -->

## Reading the numbers

- The widest gap in the suite: Skia's blurred rounded rectangle is 73-91% slower than `Apos.Shapes` everywhere, the browser included.
- Giving every panel a unique size makes no measurable difference, so Skia's cached blur is not the limit.
- For many shadows, use a shader (or bake them into a texture) rather than Skia's mask filter.
