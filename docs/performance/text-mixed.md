# Text, mixed fonts

The same labels at six sizes (12 to 64 px), so a game needs a font at each size.

- **Engine side:** A `SpriteFont` baked per size (six atlases), and FontStashSharp, which rasterizes each size on demand.
- **Skia side:** One `SKFont` per size, `DrawText` per label.

| Engine | Skia (direct) |
|---|---|
| ![Text, mixed fonts, engine](images/text-mixed-engine.jpg) | ![Text, mixed fonts, Skia](images/text-mixed-skia.jpg) |

Screenshots: 200 labels, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Mixed text 50 | 1606 (FontStashSharp) | 1019 | -37% |
| Mixed text 200 | 1381 (FontStashSharp) | 690 | -50% |
| Mixed text 1k | 810 (FontStashSharp) | 164 | -80% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Mixed text 50 | 2535 (FontStashSharp) | 1001 | -61% |
| Mixed text 200 | 2037 (FontStashSharp) | 674 | -67% |
| Mixed text 1k | 945 (FontStashSharp) | 167 | -82% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Mixed text 50 | 1874 (FontStashSharp) | 1128 | -40% |
| Mixed text 200 | 886 (FontStashSharp) | 615 | -31% |
| Mixed text 1k | 257 (FontStashSharp) | 157 | -39% |

<!-- results:end -->

## Reading the numbers

- Native is FontStashSharp, which packs every size into shared atlases. Skia is 37-82% slower on the desktop and 31-40% slower in the browser, with the gap growing with the label count.
