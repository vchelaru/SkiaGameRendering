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

## Optional technique: cache the blurred shadow as an image per (size, sigma, alpha)

This is an optional technique for users, separate from the benchmark tables above. The cached numbers measure drawing a stored image, not drawing a vector shadow each frame, so they are not comparable with the results tables or the engine columns.

Measured on H1, MonoGame DesktopGL, Skia direct, 1600x900, `--quick` windows, 3 runs per variant, draw CPU ms. Instead of `DrawRoundRect` with a blur mask filter every frame, render the blurred rounded rectangle once into a GPU surface (padded by 3 sigma), keep the snapshot in a dictionary keyed by (width, height, sigma, alpha), and `DrawImage` it at `rect.Left - pad, rect.Top - pad`.

| Scene | Mask filter blur (ms) | Cached image (ms) | Ratio |
|---|---:|---:|---:|
| Shadows 500 (5 sizes) | 40.0-42.8 | 2.0-2.1 | about 20x |
| Shadows 500 unique sizes | about 85 | about 2.1 | about 40x |
| UI 200 | 2.65-3.08 | 1.73-1.84 | about 1.6x |
| UI 1k | 11.3-12.8 | 7.9-8.1 | about 1.3x |

The 10-run UI 1k median was 10.28 ms against 7.97 ms (1.29x), hence "about 1.3x".

Pixels, measured on a CPU raster surface only (not checked on a GPU surface): drawn at integer positions the cached image is identical to the direct blur (0 differing pixels, max alpha difference 0/255). At a half-pixel x offset the linear resample softens it slightly: max alpha difference 5/255. `tests/Tests.Core/ShadowImageEquivalenceTests.cs` pins the integer-position case at zero differences.

Caveats: it pays off only when sizes repeat across frames, and the first appearance of each size costs one GPU surface render. Also tried and slower: a 9-slice shadow (nine `DrawImage` calls per shadow, UI 1k 13.4 ms, slower than the mask filter) and `SKImageFilter.CreateDropShadowOnly` (5-10x slower than the mask filter).
