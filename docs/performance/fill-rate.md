# Fill rate

10 or 40 screen-sized layers drawn over each other, so the cost is almost all pixels, not draw calls. Opaque layers, and translucent ones at 25% alpha.

- **Engine side:** `SpriteBatch.Draw` of a 1x1 white texture stretched over each layer, and `Apos.Shapes` `FillRectangle`.
- **Skia side:** `SKCanvas.DrawRect` per layer.

| Engine | Skia (direct) |
|---|---|
| ![Fill rate, engine](images/fill-rate-engine.jpg) | ![Fill rate, Skia](images/fill-rate-skia.jpg) |

Screenshots: 10 translucent layers, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Opaque 10 layers | 1321 (SpriteBatch) | 707 | 54% |
| Opaque 40 layers | 684 (SpriteBatch) | 523 | 76% |
| Translucent 10 layers | 1259 (SpriteBatch) | 704 | 56% |
| Translucent 40 layers | 661 (SpriteBatch) | 589 | 89% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Opaque 10 layers | 1570 (SpriteBatch) | 722 | 46% |
| Opaque 40 layers | 780 (SpriteBatch) | 557 | 71% |
| Translucent 10 layers | 1515 (SpriteBatch) | 756 | 50% |
| Translucent 40 layers | 761 (SpriteBatch) | 476 | 63% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Opaque 10 layers | 1452 (SpriteBatch) | 1174 | 81% |
| Opaque 40 layers | 60 (SpriteBatch) | 718 | 1197% |
| Translucent 10 layers | 298 (Apos.Shapes) | 1140 | 383% |
| Translucent 40 layers | 74 (SpriteBatch) | 643 | 869% |

<!-- results:end -->

## Reading the numbers

- Desktop: Skia runs at 46-89% of `SpriteBatch`, closer at 40 layers than at 10.
- Browser: Skia runs at 870-1200% at 40 layers, where the engine side stalls.
