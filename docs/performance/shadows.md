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

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Shadows 100 | 758 (Apos.Shapes) | 116 | 15% |
| Shadows 500 | 216 (Apos.Shapes) | 28 | 13% |
| Shadows 2k | 59 (Apos.Shapes) | 6 | 10% |
| Shadows 500 unique sizes | 206 (Apos.Shapes) | 27 | 13% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Shadows 100 | 890 (Apos.Shapes) | 106 | 12% |
| Shadows 500 | 239 (Apos.Shapes) | 28 | 12% |
| Shadows 2k | 64 (Apos.Shapes) | 6 | 9% |
| Shadows 500 unique sizes | 243 (Apos.Shapes) | 27 | 11% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Shadows 100 | 83 (Apos.Shapes) | 22 | 27% |
| Shadows 500 | 27 (Apos.Shapes) | 3 | 11% |
| Shadows 2k | 4 (Apos.Shapes) | 1 | 25% |
| Shadows 500 unique sizes | 17 (Apos.Shapes) | 2 | 12% |

<!-- results:end -->

## Reading the numbers

- The widest gap in the suite: Skia's blurred rounded rectangle runs at 9-27% of `Apos.Shapes` everywhere, the browser included.
- Giving every panel a unique size makes no measurable difference, so Skia's cached blur is not the limit.
- For many shadows, use a shader (or bake them into a texture) rather than Skia's mask filter.
