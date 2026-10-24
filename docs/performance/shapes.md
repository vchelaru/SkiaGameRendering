# Shapes

Filled circles, and a mixed scene of circles, rounded rectangles with a border, thick lines and triangles.

- **Engine side:** `Apos.Shapes`: one `ShapeBatch`, signed-distance-field shapes drawn as instanced quads.
- **Skia side:** `SKCanvas.DrawCircle`, `DrawRoundRect` (fill and stroke), `DrawLine` and `DrawPath`, anti-aliased. Rotation goes through the canvas matrix.

| Engine | Skia (direct) |
|---|---|
| ![Shapes, engine](images/shapes-engine.jpg) | ![Shapes, Skia](images/shapes-skia.jpg) |

Screenshots: mixed shapes 2k, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Circles 500 | 1017 (Apos.Shapes) | 820 | -19% |
| Circles 2k | 562 (Apos.Shapes) | 431 | -23% |
| Circles 10k | 217 (Apos.Shapes) | 101 | -53% |
| Mixed shapes 2k | 543 (Apos.Shapes) | 69 | -87% |
| Mixed shapes 10k | 146 (Apos.Shapes) | 15 | -90% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Circles 500 | 1637 (Apos.Shapes) | 834 | -49% |
| Circles 2k | 852 (Apos.Shapes) | 462 | -46% |
| Circles 10k | 270 (Apos.Shapes) | 91 | -66% |
| Mixed shapes 2k | 679 (Apos.Shapes) | 70 | -90% |
| Mixed shapes 10k | 170 (Apos.Shapes) | 15 | -91% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | Change |
|---|---:|---:|---:|
| Circles 500 | 405 (Apos.Shapes) | 804 | +99% |
| Circles 2k | 108 (Apos.Shapes) | 323 | +199% |
| Circles 10k | 22 (Apos.Shapes) | 82 | +273% |
| Mixed shapes 2k | 77 (Apos.Shapes) | 59 | -23% |
| Mixed shapes 10k | 16 (Apos.Shapes) | 13 | -19% |

<!-- results:end -->

## Reading the numbers

- Desktop: plain circles are 19-66% slower than `Apos.Shapes`; the mixed scene (strokes, round caps, paths) is about 90% slower, because those go through Skia's path rendering instead of a closed-form shader.
- Browser: Skia is two to four times faster on plain circles, since `Apos.Shapes` builds its vertices in managed WebAssembly. The mixed scene is about 20% slower.
