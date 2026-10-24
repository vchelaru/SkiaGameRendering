# UI panels

What a game UI draws: cards with a drop shadow, a rounded body and a border, an icon and a line of text each.

- **Engine side:** `Apos.Shapes` for shadows and cards, then `SpriteBatch` for the icons and the `SpriteFont` text, in two batches.
- **Skia side:** Everything on one canvas: shadow and card with `DrawRoundRect`, icons with `DrawAtlas` (one call per texture), text with `DrawText`.

| Engine | Skia (direct) |
|---|---|
| ![UI panels, engine](images/ui-engine.jpg) | ![UI panels, Skia](images/ui-skia.jpg) |

Screenshots: 200 cards, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL

**H2**: Raw 2026-10-23 at 2a3a7c6; Skia 2026-10-23 at 2a3a7c6

| Scene | Apos.Shapes + SpriteBatch FPS | Skia render target FPS | Skia direct FPS | Skia render target / Apos.Shapes + SpriteBatch | Skia direct / Apos.Shapes + SpriteBatch |
|---|---:|---:|---:|---:|---:|
| UI 50 | 319 | 663 | 796 | 208% | 250% |
| UI 200 | 215 | 300 | 374 | 140% | 174% |
| UI 1k | 40 | 77 | 78 | 193% | 195% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes + SpriteBatch | Skia render target | Skia direct |
|---|---:|---:|---:|
| UI 50 | 0.310 | 0.607 | 0.551 |
| UI 200 | 0.894 | 1.534 | 1.513 |
| UI 1k | 4.820 | 6.771 | 6.919 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes + SpriteBatch | Skia render target | Skia direct |
|---|---:|---:|---:|
| UI 50 | 1.070 / 11.359 | 1.417 / 2.042 | 1.202 / 1.529 |
| UI 200 | 1.781 / 17.616 | 3.197 / 4.145 | 2.492 / 4.018 |
| UI 1k | 7.024 / 100.129 | 12.708 / 16.882 | 12.503 / 16.249 |

</details>

### MonoGame WindowsDX

**H2**: Raw 2026-10-23 at 2a3a7c6

| Scene | Apos.Shapes + SpriteBatch FPS |
|---|---:|
| UI 50 | 770 |
| UI 200 | 243 |
| UI 1k | 53 |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes + SpriteBatch |
|---|---:|
| UI 50 | 0.068 |
| UI 200 | 0.391 |
| UI 1k | 7.342 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes + SpriteBatch |
|---|---:|
| UI 50 | 1.256 / 1.621 |
| UI 200 | 4.021 / 4.581 |
| UI 1k | 18.653 / 19.231 |

</details>

### KNI DesktopGL

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | Apos.Shapes + SpriteBatch FPS | Skia render target FPS | Skia direct FPS | Skia render target / Apos.Shapes + SpriteBatch | Skia direct / Apos.Shapes + SpriteBatch |
|---|---:|---:|---:|---:|---:|
| UI 50 | 649 | 592 | 723 | 91% | 111% |
| UI 200 | 115 | 288 | 374 | 250% | 325% |
| UI 1k | 63 | 76 | 71 | 121% | 113% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes + SpriteBatch | Skia render target | Skia direct |
|---|---:|---:|---:|
| UI 50 | 0.261 | 0.741 | 0.559 |
| UI 200 | 1.160 | 1.723 | 1.504 |
| UI 1k | 4.825 | 6.884 | 7.566 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes + SpriteBatch | Skia render target | Skia direct |
|---|---:|---:|---:|
| UI 50 | 0.598 / 4.879 | 1.680 / 2.099 | 1.344 / 1.746 |
| UI 200 | 2.473 / 31.565 | 3.349 / 4.384 | 2.511 / 3.889 |
| UI 1k | 5.826 / 53.212 | 12.922 / 17.009 | 13.681 / 17.226 |

</details>

### KNI WebGL (Chrome)

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | Apos.Shapes + SpriteBatch FPS | Skia render target FPS | Skia direct FPS | Skia render target / Apos.Shapes + SpriteBatch | Skia direct / Apos.Shapes + SpriteBatch |
|---|---:|---:|---:|---:|---:|
| UI 50 | 0* | 900 | 685 |  |  |
| UI 200 | 24 | 396 | 265 | 1650% | 1104% |
| UI 1k | 5 | 95 | 71 | 1900% | 1420% |
\* A frame took more than 20 times the median (a shader compile or a garbage collection), which drags the average down. The median is in the table below.

<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes + SpriteBatch | Skia render target | Skia direct |
|---|---:|---:|---:|
| UI 50 | 198.600 | 0.900 | 1.300 |
| UI 200 | 39.900 | 2.300 | 3.500 |
| UI 1k | 194.600 | 10.100 | 13.600 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes + SpriteBatch | Skia render target | Skia direct |
|---|---:|---:|---:|
| UI 50 | 199.400 / 62389.500 | 1.000 / 1.600 | 1.400 / 1.800 |
| UI 200 | 41.300 / 54.400 | 2.500 / 3.100 | 3.700 / 4.500 |
| UI 1k | 195.500 / 217.400 | 10.400 / 11.400 | 14.000 / 15.900 |

</details>

<!-- results:end -->

## Reading the numbers

- On KNI DesktopGL the two are close at 50 cards and Skia is ahead at 200 (about 2.5-3x); on MonoGame DesktopGL Skia is about twice as fast already at 50. The icons and text go into the same Skia pass, where the engine side needs a second batch.
- At 1k cards both are fill-bound and within about 20%.
- In the browser Skia is well ahead, since `Apos.Shapes` and `SpriteBatch` are CPU-bound in WebAssembly there.
