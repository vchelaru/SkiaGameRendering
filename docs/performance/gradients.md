# Gradients

Rounded rectangles filled with a two-color linear gradient that turns with the shape.

- **Engine side:** `Apos.Shapes` `FillRectangle` with a `Gradient` in the shape's own space (`isLocal: true`).
- **Skia side:** One `SKShader.CreateLinearGradient` per rectangle, built once, set on the paint and drawn through a translated and rotated canvas.

| Engine | Skia (direct) |
|---|---|
| ![Gradients, engine](images/gradients-engine.jpg) | ![Gradients, Skia](images/gradients-skia.jpg) |

Screenshots: 2k gradients, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Gradients 500 | 886 (Apos.Shapes) | 444 | 50% |
| Gradients 2k | 552 (Apos.Shapes) | 123 | 22% |
| Gradients 10k | 167 (Apos.Shapes) | 28 | 17% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Gradients 500 | 1451 (Apos.Shapes) | 410 | 28% |
| Gradients 2k | 711 (Apos.Shapes) | 126 | 18% |
| Gradients 10k | 191 (Apos.Shapes) | 27 | 14% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Gradients 500 | 351 (Apos.Shapes) | 316 | 90% |
| Gradients 2k | 95 (Apos.Shapes) | 99 | 104% |
| Gradients 10k | 21 (Apos.Shapes) | 22 | 105% |

<!-- results:end -->

## Reading the numbers

- Desktop: Skia runs at 14-50% of `Apos.Shapes`. Skia fills each gradient rectangle with its own shader and draw.
- Browser: about even (90-105%), because `Apos.Shapes` is CPU-bound in WebAssembly there.
