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

### MonoGame DesktopGL

**H2**: Raw 2026-10-23 at 2a3a7c6; Skia 2026-10-23 at 2a3a7c6

| Scene | Apos.Shapes FPS | Skia render target FPS | Skia direct FPS | Skia render target / Apos.Shapes | Skia direct / Apos.Shapes |
|---|---:|---:|---:|---:|---:|
| Circles 500 | 1017 | 657 | 820 | 65% | 81% |
| Circles 2k | 562 | 342 | 431 | 61% | 77% |
| Circles 10k | 217 | 88 | 101 | 41% | 47% |
| Mixed shapes 2k | 543 | 67 | 69 | 12% | 13% |
| Mixed shapes 10k | 146 | 15 | 15 | 10% | 10% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes | Skia render target | Skia direct |
|---|---:|---:|---:|
| Circles 500 | 0.097 | 0.437 | 0.390 |
| Circles 2k | 0.241 | 1.140 | 1.085 |
| Circles 10k | 2.711 | 4.956 | 4.757 |
| Mixed shapes 2k | 0.359 | 11.453 | 11.644 |
| Mixed shapes 10k | 3.334 | 58.128 | 60.868 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes | Skia render target | Skia direct |
|---|---:|---:|---:|
| Circles 500 | 0.890 / 2.341 | 1.432 / 2.006 | 1.172 / 1.537 |
| Circles 2k | 1.581 / 3.156 | 2.813 / 3.559 | 2.219 / 2.949 |
| Circles 10k | 4.273 / 6.081 | 11.027 / 13.849 | 9.879 / 12.943 |
| Mixed shapes 2k | 1.653 / 3.207 | 14.729 / 17.520 | 14.369 / 16.599 |
| Mixed shapes 10k | 6.439 / 9.213 | 65.148 / 70.437 | 66.757 / 70.799 |

</details>

### MonoGame WindowsDX

**H2**: Raw 2026-10-23 at 2a3a7c6

| Scene | Apos.Shapes FPS |
|---|---:|
| Circles 500 | 1567 |
| Circles 2k | 782 |
| Circles 10k | 235 |
| Mixed shapes 2k | 597 |
| Mixed shapes 10k | 141 |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes |
|---|---:|
| Circles 500 | 0.070 |
| Circles 2k | 0.262 |
| Circles 10k | 1.395 |
| Mixed shapes 2k | 0.610 |
| Mixed shapes 10k | 3.087 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes |
|---|---:|
| Circles 500 | 0.616 / 0.752 |
| Circles 2k | 1.244 / 1.601 |
| Circles 10k | 4.228 / 4.991 |
| Mixed shapes 2k | 1.625 / 2.049 |
| Mixed shapes 10k | 6.894 / 8.988 |

</details>

### KNI DesktopGL

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | Apos.Shapes FPS | Skia render target FPS | Skia direct FPS | Skia render target / Apos.Shapes | Skia direct / Apos.Shapes |
|---|---:|---:|---:|---:|---:|
| Circles 500 | 1637 | 693 | 834 | 42% | 51% |
| Circles 2k | 852 | 335 | 462 | 39% | 54% |
| Circles 10k | 270 | 86 | 91 | 32% | 34% |
| Mixed shapes 2k | 679 | 67 | 70 | 10% | 10% |
| Mixed shapes 10k | 170 | 15 | 15 | 9% | 9% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes | Skia render target | Skia direct |
|---|---:|---:|---:|
| Circles 500 | 0.079 | 0.551 | 0.397 |
| Circles 2k | 0.228 | 1.299 | 1.110 |
| Circles 10k | 0.955 | 5.355 | 5.496 |
| Mixed shapes 2k | 0.263 | 11.642 | 11.531 |
| Mixed shapes 10k | 3.080 | 58.894 | 58.619 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes | Skia render target | Skia direct |
|---|---:|---:|---:|
| Circles 500 | 0.599 / 0.691 | 1.441 / 1.792 | 1.159 / 1.600 |
| Circles 2k | 1.153 / 1.467 | 2.878 / 3.687 | 2.073 / 2.781 |
| Circles 10k | 3.594 / 4.235 | 11.360 / 14.123 | 10.806 / 13.908 |
| Mixed shapes 2k | 1.440 / 1.747 | 14.632 / 17.166 | 14.088 / 16.650 |
| Mixed shapes 10k | 5.655 / 7.040 | 65.310 / 69.746 | 65.103 / 69.524 |

</details>

### KNI WebGL (Chrome)

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | Apos.Shapes FPS | Skia render target FPS | Skia direct FPS | Skia render target / Apos.Shapes | Skia direct / Apos.Shapes |
|---|---:|---:|---:|---:|---:|
| Circles 500 | 405 | 1387 | 804 | 342% | 199% |
| Circles 2k | 108 | 475 | 323 | 440% | 299% |
| Circles 10k | 22 | 112 | 82 | 509% | 373% |
| Mixed shapes 2k | 77 | 88 | 59 | 114% | 77% |
| Mixed shapes 10k | 16 | 18 | 13 | 113% | 81% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes | Skia render target | Skia direct |
|---|---:|---:|---:|
| Circles 500 | 1.200 | 0.500 | 1.100 |
| Circles 2k | 5.200 | 1.900 | 2.800 |
| Circles 10k | 25.400 | 8.400 | 11.700 |
| Mixed shapes 2k | 6.300 | 10.900 | 16.400 |
| Mixed shapes 10k | 72.400 | 54.000 | 77.800 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes | Skia render target | Skia direct |
|---|---:|---:|---:|
| Circles 500 | 1.800 / 4.700 | 0.700 / 1.100 | 1.200 / 1.600 |
| Circles 2k | 6.400 / 30.800 | 2.000 / 2.900 | 3.000 / 3.700 |
| Circles 10k | 31.500 / 88.800 | 8.700 / 12.000 | 12.000 / 13.300 |
| Mixed shapes 2k | 8.300 / 53.200 | 11.200 / 12.700 | 16.800 / 18.800 |
| Mixed shapes 10k | 74.200 / 103.100 | 54.500 / 57.500 | 78.400 / 83.300 |

</details>

<!-- results:end -->

## Reading the numbers

- On the desktop backends `Apos.Shapes` is faster in every shape scene. Plain circles run Skia at roughly 30-80% of Apos; the mixed scene, with strokes, round caps and paths, runs at about 10%, because those go through Skia's path rendering rather than a closed-form shader.
- In the browser the order flips for plain circles: `Apos.Shapes` builds its vertices in managed code compiled to WebAssembly, and Skia is native, so Skia is two to five times faster there. The mixed scene is about even.
- The render-target and direct rows are close on the desktop. In the browser the direct canvas is about 20% slower than the render target on the mixed scenes; a likely cause is the state reset between Skia and KNI that each direct pass pays.
