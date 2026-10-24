# SVG

The drop icon from `samples/Shared/Drop.svg` (a path with a linear gradient and a stroke), drawn at 24 to 96 px as vectors on every frame.

- **Engine side:** `Apos.Shapes` `ShapeBatch.DrawSvg` on a parsed `ShapeSvg`. The "pre-rasterized" row draws the SVG once into a texture and then draws that with `SpriteBatch`.
- **Skia side:** `Svg.Skia` parses the file to an `SKPicture`; every frame replays it with `DrawPicture`. The "pre-rasterized" rows draw a bitmap rendered once, through `DrawAtlas`.

| Engine | Skia (direct) |
|---|---|
| ![SVG, engine](images/svg-engine.jpg) | ![SVG, Skia](images/svg-skia.jpg) |

Screenshots: 500 drops, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL

**H2**: Raw 2026-10-23 at 2a3a7c6; Skia 2026-10-23 at 2a3a7c6

| Scene | Apos.Shapes SVG FPS | SpriteBatch (pre-rasterized SVG) FPS | Skia render target FPS | Skia direct FPS | Skia render target (pre-rasterized SVG) FPS | Skia direct (pre-rasterized SVG) FPS | Skia render target / Apos.Shapes SVG | Skia direct / Apos.Shapes SVG |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| SVG 100 | 655 | 2080 | 63 | 74 | 1043 | 1084 | 10% | 11% |
| SVG 500 | 144 | 1038 | 19 | 18 | 801 | 834 | 13% | 13% |
| SVG 2k | 38 | 589 | 4 | 4 | 404 | 502 | 11% | 11% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes SVG | SpriteBatch (pre-rasterized SVG) | Skia render target | Skia direct | Skia render target (pre-rasterized SVG) | Skia direct (pre-rasterized SVG) |
|---|---:|---:|---:|---:|---:|---:|
| SVG 100 | 0.829 | 0.029 | 13.367 | 10.321 | 0.187 | 0.150 |
| SVG 500 | 5.709 | 0.082 | 46.884 | 47.410 | 0.201 | 0.153 |
| SVG 2k | 22.669 | 0.167 | 261.490 | 262.920 | 0.379 | 0.374 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes SVG | SpriteBatch (pre-rasterized SVG) | Skia render target | Skia direct | Skia render target (pre-rasterized SVG) | Skia direct (pre-rasterized SVG) |
|---|---:|---:|---:|---:|---:|---:|
| SVG 100 | 1.376 / 2.847 | 0.425 / 0.653 | 15.815 / 17.331 | 13.334 / 15.658 | 0.932 / 1.301 | 0.904 / 1.146 |
| SVG 500 | 6.757 / 8.787 | 0.853 / 2.385 | 53.120 / 56.747 | 53.883 / 57.484 | 1.225 / 1.496 | 1.184 / 1.458 |
| SVG 2k | 23.257 / 33.410 | 1.471 / 3.165 | 261.900 / 267.180 | 263.207 / 271.994 | 2.418 / 2.890 | 1.959 / 2.323 |

</details>

### MonoGame WindowsDX

**H2**: Raw 2026-10-23 at 2a3a7c6

| Scene | Apos.Shapes SVG FPS | SpriteBatch (pre-rasterized SVG) FPS |
|---|---:|---:|
| SVG 100 | 604 | 2317 |
| SVG 500 | 151 | 1694 |
| SVG 2k | 39 | 842 |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes SVG | SpriteBatch (pre-rasterized SVG) |
|---|---:|---:|
| SVG 100 | 0.644 | 0.010 |
| SVG 500 | 4.308 | 0.028 |
| SVG 2k | 20.946 | 0.105 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes SVG | SpriteBatch (pre-rasterized SVG) |
|---|---:|---:|
| SVG 100 | 1.613 / 2.036 | 0.417 / 0.556 |
| SVG 500 | 6.631 / 7.442 | 0.572 / 0.712 |
| SVG 2k | 25.750 / 30.932 | 1.159 / 1.501 |

</details>

### KNI DesktopGL

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | Apos.Shapes SVG FPS | SpriteBatch (pre-rasterized SVG) FPS | Skia render target FPS | Skia direct FPS | Skia render target (pre-rasterized SVG) FPS | Skia direct (pre-rasterized SVG) FPS | Skia render target / Apos.Shapes SVG | Skia direct / Apos.Shapes SVG |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| SVG 100 | 655 | 2381 | 63 | 73 | 1053 | 1158 | 10% | 11% |
| SVG 500 | 161 | 1735 | 18 | 18 | 774 | 912 | 11% | 11% |
| SVG 2k | 32 | 839 | 4 | 4 | 339 | 502 | 13% | 13% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes SVG | SpriteBatch (pre-rasterized SVG) | Skia render target | Skia direct | Skia render target (pre-rasterized SVG) | Skia direct (pre-rasterized SVG) |
|---|---:|---:|---:|---:|---:|---:|
| SVG 100 | 0.742 | 0.034 | 13.621 | 10.384 | 0.310 | 0.145 |
| SVG 500 | 5.160 | 0.054 | 47.867 | 47.657 | 0.367 | 0.156 |
| SVG 2k | 18.580 | 0.159 | 263.136 | 261.971 | 0.619 | 0.332 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes SVG | SpriteBatch (pre-rasterized SVG) | Skia render target | Skia direct | Skia render target (pre-rasterized SVG) | Skia direct (pre-rasterized SVG) |
|---|---:|---:|---:|---:|---:|---:|
| SVG 100 | 1.505 / 1.959 | 0.408 / 0.536 | 15.816 / 17.814 | 13.336 / 15.911 | 0.929 / 1.213 | 0.864 / 1.091 |
| SVG 500 | 6.199 / 8.953 | 0.561 / 0.674 | 54.281 / 57.647 | 54.331 / 60.112 | 1.270 / 1.547 | 1.019 / 1.450 |
| SVG 2k | 19.195 / 104.068 | 1.155 / 1.512 | 263.541 / 267.238 | 262.411 / 269.328 | 2.889 / 3.292 | 1.956 / 2.297 |

</details>

### KNI WebGL (Chrome)

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | Apos.Shapes SVG FPS | SpriteBatch (pre-rasterized SVG) FPS | Skia render target FPS | Skia direct FPS | Skia render target (pre-rasterized SVG) FPS | Skia direct (pre-rasterized SVG) FPS | Skia render target / Apos.Shapes SVG | Skia direct / Apos.Shapes SVG |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| SVG 100 | 214 | 2502 | 517 | 354 | 2564 | 1473 | 242% | 165% |
| SVG 500 | 49 | 1848 | 106 | 73 | 1613 | 1286 | 216% | 149% |
| SVG 2k | 13 | 994 | 35 | 25 | 794 | 709 | 269% | 192% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | Apos.Shapes SVG | SpriteBatch (pre-rasterized SVG) | Skia render target | Skia direct | Skia render target (pre-rasterized SVG) | Skia direct (pre-rasterized SVG) |
|---|---:|---:|---:|---:|---:|---:|
| SVG 100 | 3.800 | 0.100 | 1.600 | 2.600 | 0.200 | 0.500 |
| SVG 500 | 18.800 | 0.100 | 7.800 | 12.500 | 0.300 | 0.600 |
| SVG 2k | 75.300 | 0.300 | 28.000 | 40.100 | 0.700 | 1.200 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | Apos.Shapes SVG | SpriteBatch (pre-rasterized SVG) | Skia render target | Skia direct | Skia render target (pre-rasterized SVG) | Skia direct (pre-rasterized SVG) |
|---|---:|---:|---:|---:|---:|---:|
| SVG 100 | 4.200 / 6.700 | 0.200 / 0.300 | 1.700 / 3.000 | 2.800 / 3.300 | 0.300 / 0.700 | 0.700 / 0.900 |
| SVG 500 | 19.200 / 25.000 | 0.200 / 0.400 | 8.100 / 15.700 | 12.800 / 19.200 | 0.500 / 1.200 | 0.700 / 1.100 |
| SVG 2k | 76.100 / 112.000 | 0.500 / 1.300 | 28.500 / 30.000 | 40.600 / 43.200 | 1.000 / 2.500 | 1.400 / 1.900 |

</details>

<!-- results:end -->

## Reading the numbers

- Vector against vector on the desktop: Skia replays the picture at about 4 FPS for 2k icons (18 for 500), `Apos.Shapes` at 32 (161). Skia's path and stroke tessellation is what costs.
- In the browser the order reverses (Skia 35 FPS against 13 for 2k icons), for the same reason as the shapes page.
- Pre-rasterized, both are fast: `SpriteBatch` 839 FPS and Skia 340-500 for 2k icons on KNI DesktopGL. Pre-rasterizing at the sizes you need is the practical fix for many vector icons in Skia.
