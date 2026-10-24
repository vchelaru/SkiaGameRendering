# Sprites, one texture

One 64x64 soft-edged disc, drawn thousands of times with scale, rotation and (in the last scene) a per-sprite tint. This is the case `SpriteBatch` is built for, so it shows the cost of going through Skia when you do not need to.

- **Engine side:** `SpriteBatch.Draw` in one deferred batch.
- **Skia side:** One `DrawAtlas` call on a GPU-resident image, tinted through the `colors` array with `SKBlendMode.Modulate`. The "DrawImage per sprite" rows draw the same sprites with one `DrawImage` each (and a pre-built `SKColorFilter` per tint) to show why `DrawAtlas` is the path to use.

| Engine | Skia (direct) |
|---|---|
| ![Sprites, one texture, engine](images/sprites-single-engine.jpg) | ![Sprites, one texture, Skia](images/sprites-single-skia.jpg) |

Screenshots: 2k sprites, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL

**H1**: Raw 2026-09-30 at 59d06ca; Skia 2026-09-30 at 59d06ca

| Scene | SpriteBatch FPS | Skia render target FPS | Skia render target / SpriteBatch |
|---|---:|---:|---:|
| Sprites 0 | 2190 | 1830 | 84% |
| Sprites 500 | 2677 | 1440 | 54% |
| Sprites 2k | 2036 | 1327 | 65% |
| Sprites 10k | 672 | 573 | 85% |
| Sprites 50k | 151 | 159 | 105% |
| Tinted sprites 10k | 701 | 569 | 81% |
**H2**: Raw 2026-10-23 at 2a3a7c6; Skia 2026-10-23 at 2a3a7c6

| Scene | SpriteBatch FPS | Skia render target FPS | Skia direct FPS | Skia direct (DrawImage per sprite) FPS | Skia render target / SpriteBatch | Skia direct / SpriteBatch |
|---|---:|---:|---:|---:|---:|---:|
| Sprites 0 | 1680 | 963 | 1634 | 1712 | 57% | 97% |
| Sprites 500 | 1433 | 713 | 941 | 734 | 50% | 66% |
| Sprites 2k | 1021 | 470 | 600 | 337 | 46% | 59% |
| Sprites 10k | 270 | 239 | 270 | 78 | 89% | 100% |
| Sprites 50k | 62 | 50 | 52 | 20 | 81% | 84% |
| Tinted sprites 10k | 325 | 239 | 267 | 44 | 74% | 82% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch | Skia render target | Skia direct | Skia direct (DrawImage per sprite) |
|---|---:|---:|---:|---:|
| Sprites 0 | 0.009 | 0.154 | 0.094 | 0.065 |
| Sprites 500 | 0.077 | 0.191 | 0.145 | 0.476 |
| Sprites 2k | 0.157 | 0.359 | 0.279 | 1.405 |
| Sprites 10k | 0.762 | 0.857 | 0.709 | 6.732 |
| Sprites 50k | 3.651 | 4.097 | 4.338 | 34.813 |
| Tinted sprites 10k | 0.765 | 1.025 | 0.945 | 17.923 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteBatch | Skia render target | Skia direct | Skia direct (DrawImage per sprite) |
|---|---:|---:|---:|---:|
| Sprites 0 | 0.496 / 1.069 | 1.014 / 1.325 | 0.588 / 0.801 | 0.547 / 0.805 |
| Sprites 500 | 0.594 / 1.454 | 1.358 / 1.728 | 1.029 / 1.363 | 1.292 / 1.853 |
| Sprites 2k | 0.862 / 2.273 | 2.071 / 2.510 | 1.585 / 2.089 | 2.786 / 4.153 |
| Sprites 10k | 3.306 / 5.561 | 4.028 / 4.979 | 3.571 / 4.515 | 12.625 / 16.027 |
| Sprites 50k | 16.185 / 18.856 | 19.754 / 22.617 | 18.987 / 22.365 | 49.904 / 53.719 |
| Tinted sprites 10k | 2.784 / 4.714 | 4.047 / 4.864 | 3.633 / 4.458 | 22.538 / 24.787 |

</details>

### MonoGame WindowsDX

**H1**: Raw 2026-09-30 at 59d06ca; Skia 2026-09-30 at 59d06ca

| Scene | SpriteBatch FPS | Skia render target FPS | Skia render target / SpriteBatch |
|---|---:|---:|---:|
| Sprites 0 | 4003 | 3231 | 81% |
| Sprites 500 | 4224 | 2489 | 59% |
| Sprites 2k | 2749 | 1972 | 72% |
| Sprites 10k | 946 | 524 | 55% |
| Sprites 50k | 181 | 84 | 46% |
| Tinted sprites 10k | 877 | 589 | 67% |
**H2**: Raw 2026-10-23 at 2a3a7c6

| Scene | SpriteBatch FPS |
|---|---:|
| Sprites 0 | 2677 |
| Sprites 500 | 1973 |
| Sprites 2k | 1281 |
| Sprites 10k | 362 |
| Sprites 50k | 74 |
| Tinted sprites 10k | 392 |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch |
|---|---:|
| Sprites 0 | 0.001 |
| Sprites 500 | 0.030 |
| Sprites 2k | 0.100 |
| Sprites 10k | 0.629 |
| Sprites 50k | 2.864 |
| Tinted sprites 10k | 0.496 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteBatch |
|---|---:|
| Sprites 0 | 0.358 / 0.442 |
| Sprites 500 | 0.497 / 0.615 |
| Sprites 2k | 0.763 / 0.958 |
| Sprites 10k | 2.682 / 3.250 |
| Sprites 50k | 13.574 / 14.431 |
| Tinted sprites 10k | 2.484 / 2.987 |

</details>

### KNI DesktopGL

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | SpriteBatch FPS | Skia render target FPS | Skia direct FPS | Skia direct (DrawImage per sprite) FPS | Skia render target / SpriteBatch | Skia direct / SpriteBatch |
|---|---:|---:|---:|---:|---:|---:|
| Sprites 0 | 2132 | 983 | 1741 | 1698 | 46% | 82% |
| Sprites 500 | 1945 | 711 | 949 | 753 | 37% | 49% |
| Sprites 2k | 1335 | 461 | 633 | 347 | 35% | 47% |
| Sprites 10k | 364 | 250 | 244 | 78 | 69% | 67% |
| Sprites 50k | 76 | 51 | 52 | 20 | 67% | 68% |
| Tinted sprites 10k | 399 | 240 | 272 | 43 | 60% | 68% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch | Skia render target | Skia direct | Skia direct (DrawImage per sprite) |
|---|---:|---:|---:|---:|
| Sprites 0 | 0.007 | 0.283 | 0.067 | 0.094 |
| Sprites 500 | 0.058 | 0.364 | 0.184 | 0.468 |
| Sprites 2k | 0.127 | 0.453 | 0.291 | 1.382 |
| Sprites 10k | 0.562 | 0.837 | 0.931 | 6.689 |
| Sprites 50k | 3.269 | 5.875 | 4.945 | 34.622 |
| Tinted sprites 10k | 0.617 | 1.107 | 0.943 | 18.229 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteBatch | Skia render target | Skia direct | Skia direct (DrawImage per sprite) |
|---|---:|---:|---:|---:|
| Sprites 0 | 0.454 / 0.552 | 0.993 / 1.337 | 0.541 / 0.785 | 0.573 / 0.684 |
| Sprites 500 | 0.493 / 0.724 | 1.367 / 1.722 | 1.032 / 1.292 | 1.264 / 1.815 |
| Sprites 2k | 0.732 / 0.912 | 2.098 / 2.524 | 1.530 / 1.879 | 2.719 / 4.054 |
| Sprites 10k | 2.684 / 3.211 | 3.837 / 4.666 | 4.035 / 4.725 | 12.412 / 16.676 |
| Sprites 50k | 13.239 / 13.808 | 19.804 / 22.059 | 18.977 / 21.865 | 49.790 / 52.665 |
| Tinted sprites 10k | 2.443 / 2.873 | 3.992 / 4.907 | 3.542 / 4.361 | 22.933 / 25.479 |

</details>

### KNI WebGL (Chrome)

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | SpriteBatch FPS | Skia render target FPS | Skia direct FPS | Skia direct (DrawImage per sprite) FPS | Skia render target / SpriteBatch | Skia direct / SpriteBatch |
|---|---:|---:|---:|---:|---:|---:|
| Sprites 0 | 2759 | 3344 | 1689 | 1721 | 121% | 61% |
| Sprites 500 | 1522 | 2050 | 1393 | 960 | 135% | 92% |
| Sprites 2k | 1025 | 1063 | 1064 | 428 | 104% | 104% |
| Sprites 10k | 241 | 322 | 322 | 99 | 134% | 134% |
| Sprites 50k | 66 | 69 | 72 | 21 | 105% | 109% |
| Tinted sprites 10k | 379 | 340 | 348 | 37 | 90% | 92% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch | Skia render target | Skia direct | Skia direct (DrawImage per sprite) |
|---|---:|---:|---:|---:|
| Sprites 0 | 0.000 | 0.100 | 0.400 | 0.400 |
| Sprites 500 | 0.200 | 0.300 | 0.600 | 0.900 |
| Sprites 2k | 0.600 | 0.600 | 0.800 | 2.200 |
| Sprites 10k | 3.200 | 2.000 | 2.900 | 9.600 |
| Sprites 50k | 6.400 | 8.000 | 13.400 | 48.000 |
| Tinted sprites 10k | 1.100 | 1.200 | 2.600 | 26.100 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteBatch | Skia render target | Skia direct | Skia direct (DrawImage per sprite) |
|---|---:|---:|---:|---:|
| Sprites 0 | 0.300 / 0.800 | 0.300 / 0.500 | 0.600 / 0.800 | 0.600 / 0.800 |
| Sprites 500 | 0.600 / 1.100 | 0.400 / 0.900 | 0.700 / 1.000 | 1.000 / 1.300 |
| Sprites 2k | 0.900 / 1.500 | 0.900 / 1.700 | 0.900 / 1.300 | 2.300 / 2.700 |
| Sprites 10k | 3.800 / 6.200 | 2.300 / 6.200 | 3.000 / 3.900 | 9.900 / 11.400 |
| Sprites 50k | 6.800 / 34.900 | 8.300 / 35.900 | 13.800 / 17.800 | 48.500 / 51.300 |
| Tinted sprites 10k | 1.300 / 21.400 | 1.400 / 6.400 | 2.800 / 3.700 | 26.500 / 28.800 |

</details>

<!-- results:end -->

## Reading the numbers

- Below about 2k sprites the fixed cost of a Skia pass (context switch, flush, and for a render target the full-screen clear and composite) dominates. The 0-sprite row is that cost alone: a render target adds roughly 0.15-0.3 ms per frame, a direct canvas roughly 0.07-0.1 ms (median draw CPU time, in the second table).
- From 10k sprites up, `DrawAtlas` reaches roughly 65-100% of `SpriteBatch` on the desktop backends. Direct and render target converge there because the per-sprite work dwarfs the composite.
- One `DrawImage` per sprite is about three times slower than `DrawAtlas` at 10k sprites, and with a per-sprite color filter about six times slower. Tinting through `DrawAtlas`'s `colors` costs almost nothing.
- In the browser, `SpriteBatch` runs as AOT-compiled .NET in WebAssembly while Skia is native code, so the per-sprite cost gap narrows.
