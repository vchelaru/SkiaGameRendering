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

### MonoGame DesktopGL

**H2**: Raw 2026-10-23 at 2a3a7c6; Skia 2026-10-23 at 2a3a7c6

| Scene | SpriteFont FPS | FontStashSharp FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteFont | Skia direct / SpriteFont |
|---|---:|---:|---:|---:|---:|---:|
| Text 50 | 1679 | 1635 | 887 | 1026 | 53% | 61% |
| Text 200 | 1630 | 1636 | 546 | 673 | 33% | 41% |
| Text 1k | 1093 | 858 | 171 | 199 | 16% | 18% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Text 50 | 0.067 | 0.066 | 0.337 | 0.321 |
| Text 200 | 0.134 | 0.185 | 0.788 | 0.726 |
| Text 1k | 0.650 | 0.939 | 3.044 | 2.963 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Text 50 | 0.519 / 1.049 | 0.532 / 0.761 | 1.108 / 1.404 | 0.936 / 1.287 |
| Text 200 | 0.529 / 0.947 | 0.530 / 0.973 | 1.711 / 2.576 | 1.453 / 1.805 |
| Text 1k | 0.839 / 1.342 | 1.111 / 1.460 | 5.453 / 8.447 | 4.754 / 6.818 |

</details>

### MonoGame WindowsDX

**H2**: Raw 2026-10-23 at 2a3a7c6

| Scene | SpriteFont FPS | FontStashSharp FPS |
|---|---:|---:|
| Text 50 | 2511 | 2535 |
| Text 200 | 2239 | 2261 |
| Text 1k | 1180 | 960 |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteFont | FontStashSharp |
|---|---:|---:|
| Text 50 | 0.036 | 0.051 |
| Text 200 | 0.117 | 0.166 |
| Text 1k | 0.681 | 0.852 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteFont | FontStashSharp |
|---|---:|---:|
| Text 50 | 0.385 / 0.452 | 0.382 / 0.443 |
| Text 200 | 0.433 / 0.492 | 0.428 / 0.522 |
| Text 1k | 0.818 / 1.093 | 0.998 / 1.322 |

</details>

### KNI DesktopGL

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | SpriteFont FPS | FontStashSharp FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteFont | Skia direct / SpriteFont |
|---|---:|---:|---:|---:|---:|---:|
| Text 50 | 2627 | 2641 | 782 | 1053 | 30% | 40% |
| Text 200 | 2295 | 2295 | 530 | 747 | 23% | 33% |
| Text 1k | 1383 | 997 | 171 | 190 | 12% | 14% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Text 50 | 0.052 | 0.067 | 0.506 | 0.315 |
| Text 200 | 0.115 | 0.193 | 0.934 | 0.730 |
| Text 1k | 0.529 | 0.821 | 3.165 | 2.897 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Text 50 | 0.370 / 0.453 | 0.368 / 0.444 | 1.253 / 1.529 | 0.922 / 1.252 |
| Text 200 | 0.423 / 0.527 | 0.423 / 0.593 | 1.836 / 2.288 | 1.286 / 1.661 |
| Text 1k | 0.695 / 0.953 | 0.954 / 1.236 | 5.459 / 8.701 | 4.988 / 7.582 |

</details>

### KNI WebGL (Chrome)

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | SpriteFont FPS | FontStashSharp FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteFont | Skia direct / SpriteFont |
|---|---:|---:|---:|---:|---:|---:|
| Text 50 | 1882 | 2112 | 1281 | 1082 | 68% | 57% |
| Text 200 | 1520 | 972 | 677 | 611 | 45% | 40% |
| Text 1k | 509 | 268 | 174 | 162 | 34% | 32% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Text 50 | 0.200 | 0.300 | 0.600 | 0.700 |
| Text 200 | 0.400 | 0.800 | 1.300 | 1.400 |
| Text 1k | 1.600 | 3.300 | 5.300 | 5.700 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Text 50 | 0.300 / 0.900 | 0.400 / 0.700 | 0.700 / 1.100 | 0.900 / 1.300 |
| Text 200 | 0.600 / 1.100 | 0.900 / 1.700 | 1.400 / 1.800 | 1.600 / 2.000 |
| Text 1k | 1.800 / 2.800 | 3.500 / 4.700 | 5.500 / 6.900 | 6.000 / 7.500 |

</details>

<!-- results:end -->

## Reading the numbers

- `SpriteFont` is the fastest at every count, and the gap grows with the count: at 1k labels Skia runs at 12-14% of it on the desktop (171-190 FPS against 1383).
- FontStashSharp is close to `SpriteFont` on the desktop and slower at 1k labels.
- The browser narrows the gap to roughly a third to two thirds.
- Skia draws text at any size, with no atlas to bake; see the mixed-fonts page for where that matters.
