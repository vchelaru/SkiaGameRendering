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

### MonoGame DesktopGL

**H2**: Raw 2026-10-23 at 2a3a7c6; Skia 2026-10-23 at 2a3a7c6

| Scene | SpriteFont FPS | FontStashSharp FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteFont | Skia direct / SpriteFont |
|---|---:|---:|---:|---:|---:|---:|
| Mixed text 50 | 1590 | 1606 | 901 | 1019 | 57% | 64% |
| Mixed text 200 | 1282 | 1381 | 500 | 690 | 39% | 54% |
| Mixed text 1k | 417 | 810 | 155 | 164 | 37% | 39% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Mixed text 50 | 0.133 | 0.066 | 0.342 | 0.313 |
| Mixed text 200 | 0.499 | 0.229 | 0.793 | 0.730 |
| Mixed text 1k | 2.164 | 0.950 | 3.100 | 3.084 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Mixed text 50 | 0.546 / 0.853 | 0.543 / 0.797 | 1.065 / 1.439 | 0.956 / 1.263 |
| Mixed text 200 | 0.678 / 1.365 | 0.619 / 1.717 | 1.862 / 2.773 | 1.386 / 1.808 |
| Mixed text 1k | 2.337 / 2.619 | 1.116 / 2.058 | 6.110 / 8.826 | 5.681 / 8.674 |

</details>

### MonoGame WindowsDX

**H2**: Raw 2026-10-23 at 2a3a7c6

| Scene | SpriteFont FPS | FontStashSharp FPS |
|---|---:|---:|
| Mixed text 50 | 2356 | 2455 |
| Mixed text 200 | 1938 | 2032 |
| Mixed text 1k | 781 | 900 |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteFont | FontStashSharp |
|---|---:|---:|
| Mixed text 50 | 0.049 | 0.082 |
| Mixed text 200 | 0.232 | 0.172 |
| Mixed text 1k | 1.080 | 0.899 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteFont | FontStashSharp |
|---|---:|---:|
| Mixed text 50 | 0.408 / 0.516 | 0.394 / 0.477 |
| Mixed text 200 | 0.500 / 0.588 | 0.476 / 0.587 |
| Mixed text 1k | 1.237 / 1.535 | 1.048 / 1.485 |

</details>

### KNI DesktopGL

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | SpriteFont FPS | FontStashSharp FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteFont | Skia direct / SpriteFont |
|---|---:|---:|---:|---:|---:|---:|
| Mixed text 50 | 2512 | 2535 | 841 | 1001 | 33% | 40% |
| Mixed text 200 | 1622 | 2037 | 503 | 674 | 31% | 42% |
| Mixed text 1k | 426 | 945 | 166 | 167 | 39% | 39% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Mixed text 50 | 0.169 | 0.068 | 0.459 | 0.324 |
| Mixed text 200 | 0.449 | 0.181 | 0.927 | 0.749 |
| Mixed text 1k | 2.134 | 0.875 | 3.230 | 3.024 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Mixed text 50 | 0.388 / 0.505 | 0.384 / 0.444 | 1.173 / 1.386 | 0.969 / 1.301 |
| Mixed text 200 | 0.595 / 0.751 | 0.477 / 0.648 | 1.940 / 2.375 | 1.367 / 2.200 |
| Mixed text 1k | 2.276 / 2.616 | 1.011 / 1.230 | 5.799 / 8.502 | 5.618 / 8.484 |

</details>

### KNI WebGL (Chrome)

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | SpriteFont FPS | FontStashSharp FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteFont | Skia direct / SpriteFont |
|---|---:|---:|---:|---:|---:|---:|
| Mixed text 50 | 1258 | 1874 | 1243 | 1128 | 99% | 90% |
| Mixed text 200 | 478 | 886 | 692 | 615 | 145% | 129% |
| Mixed text 1k | 111 | 257 | 182 | 157 | 164% | 141% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Mixed text 50 | 0.600 | 0.300 | 0.600 | 0.700 |
| Mixed text 200 | 1.800 | 0.900 | 1.300 | 1.400 |
| Mixed text 1k | 8.500 | 3.500 | 5.200 | 5.800 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteFont | FontStashSharp | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Mixed text 50 | 0.700 / 1.200 | 0.500 / 0.900 | 0.700 / 1.300 | 0.800 / 1.300 |
| Mixed text 200 | 2.000 / 2.800 | 1.000 / 1.800 | 1.400 / 1.800 | 1.600 / 2.000 |
| Mixed text 1k | 8.800 / 10.500 | 3.800 / 4.800 | 5.400 / 6.300 | 6.100 / 8.000 |

</details>

<!-- results:end -->

## Reading the numbers

- With six baked `SpriteFont`s, 1k labels fall to 426 FPS on KNI DesktopGL (probably the texture switches between labels, which use different atlases); FontStashSharp keeps 945 FPS by packing sizes into shared atlases.
- At 1k labels Skia holds about 166 FPS on the desktop, below both. In the browser it overtakes baked `SpriteFont` at 1k labels (182 against 111 FPS) but not FontStashSharp (257).
