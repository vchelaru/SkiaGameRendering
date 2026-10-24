# Sprites, ten textures

Sprite `i` uses texture `i % 10`, so the draw order is texture 1, 2, ..., 10, then 1, 2, ... again. Nothing is random, so every run and platform draws the same thing. A game that draws in submission order pays for a texture change on almost every sprite.

- **Engine side:** `SpriteBatch` in submission order (`SpriteSortMode.Deferred`), and again sorted by texture (`SpriteSortMode.Texture`), which batches each texture's sprites.
- **Skia side:** One `DrawAtlas` call per texture (ten calls), each drawing every tenth sprite. That changes the draw order among sprites, as sorting by texture does.

| Engine | Skia (direct) |
|---|---|
| ![Sprites, ten textures, engine](images/sprites-textures-engine.jpg) | ![Sprites, ten textures, Skia](images/sprites-textures-skia.jpg) |

Screenshots: 3k sprites, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL

**H2**: Raw 2026-10-23 at 2a3a7c6; Skia 2026-10-23 at 2a3a7c6

| Scene | SpriteBatch (in order) FPS | SpriteBatch (sorted by texture) FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteBatch (in order) | Skia direct / SpriteBatch (in order) |
|---|---:|---:|---:|---:|---:|---:|
| Textures 0 | 2523 | 2698 | 976 | 1668 | 39% | 66% |
| Textures 300 | 1447 | 1438 | 773 | 1010 | 53% | 70% |
| Textures 3k | 172 | 606 | 372 | 491 | 216% | 285% |
| Textures 30k | 20 | 110 | 64 | 65 | 320% | 325% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch (in order) | SpriteBatch (sorted by texture) | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Textures 0 | 0.006 | 0.006 | 0.146 | 0.085 |
| Textures 300 | 0.538 | 0.095 | 0.240 | 0.192 |
| Textures 3k | 5.348 | 0.390 | 0.537 | 0.345 |
| Textures 30k | 49.089 | 6.880 | 2.616 | 2.811 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteBatch (in order) | SpriteBatch (sorted by texture) | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Textures 0 | 0.344 / 0.519 | 0.329 / 0.464 | 1.015 / 1.284 | 0.576 / 0.801 |
| Textures 300 | 0.661 / 0.916 | 0.605 / 1.479 | 1.253 / 1.596 | 0.959 / 1.227 |
| Textures 3k | 5.581 / 7.296 | 1.470 / 3.385 | 2.666 / 3.051 | 1.962 / 2.419 |
| Textures 30k | 49.465 / 52.633 | 9.221 / 10.813 | 15.456 / 16.555 | 15.493 / 16.747 |

</details>

### MonoGame WindowsDX

**H2**: Raw 2026-10-23 at 2a3a7c6

| Scene | SpriteBatch (in order) FPS | SpriteBatch (sorted by texture) FPS |
|---|---:|---:|
| Textures 0 | 2575 | 2674 |
| Textures 300 | 2170 | 2065 |
| Textures 3k | 632 | 950 |
| Textures 30k | 70 | 122 |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch (in order) | SpriteBatch (sorted by texture) |
|---|---:|---:|
| Textures 0 | 0.001 | 0.001 |
| Textures 300 | 0.160 | 0.034 |
| Textures 3k | 1.361 | 0.309 |
| Textures 30k | 13.473 | 3.685 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteBatch (in order) | SpriteBatch (sorted by texture) |
|---|---:|---:|
| Textures 0 | 0.380 / 0.447 | 0.358 / 0.440 |
| Textures 300 | 0.446 / 0.519 | 0.467 / 0.590 |
| Textures 3k | 1.520 / 2.044 | 1.030 / 1.315 |
| Textures 30k | 14.005 / 16.757 | 8.165 / 8.868 |

</details>

### KNI DesktopGL

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | SpriteBatch (in order) FPS | SpriteBatch (sorted by texture) FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteBatch (in order) | Skia direct / SpriteBatch (in order) |
|---|---:|---:|---:|---:|---:|---:|
| Textures 0 | 3053 | 3062 | 1025 | 1695 | 34% | 56% |
| Textures 300 | 1573 | 2111 | 787 | 1027 | 50% | 65% |
| Textures 3k | 199 | 949 | 368 | 480 | 185% | 241% |
| Textures 30k | 21 | 124 | 56 | 58 | 267% | 276% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch (in order) | SpriteBatch (sorted by texture) | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Textures 0 | 0.006 | 0.007 | 0.275 | 0.087 |
| Textures 300 | 0.479 | 0.072 | 0.366 | 0.183 |
| Textures 3k | 4.616 | 0.354 | 0.564 | 0.383 |
| Textures 30k | 47.119 | 3.612 | 3.580 | 3.243 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteBatch (in order) | SpriteBatch (sorted by texture) | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Textures 0 | 0.318 / 0.387 | 0.317 / 0.379 | 1.001 / 1.338 | 0.565 / 0.767 |
| Textures 300 | 0.593 / 0.828 | 0.462 / 0.538 | 1.241 / 1.586 | 0.945 / 1.177 |
| Textures 3k | 4.758 / 6.895 | 1.035 / 1.333 | 2.621 / 3.047 | 1.996 / 2.445 |
| Textures 30k | 47.378 / 50.753 | 7.979 / 8.633 | 17.862 / 18.764 | 17.322 / 18.300 |

</details>

### KNI WebGL (Chrome)

**H2**: Raw 2026-10-23 at 1df31c3; Skia 2026-10-23 at 1df31c3

| Scene | SpriteBatch (in order) FPS | SpriteBatch (sorted by texture) FPS | Skia render target FPS | Skia direct FPS | Skia render target / SpriteBatch (in order) | Skia direct / SpriteBatch (in order) |
|---|---:|---:|---:|---:|---:|---:|
| Textures 0 | 2724 | 2806 | 3210 | 1677 | 118% | 62% |
| Textures 300 | 414 | 3069 | 2268 | 1434 | 548% | 346% |
| Textures 3k | 40 | 534 | 825 | 848 | 2063% | 2120% |
| Textures 30k | 2 | 111 | 113 | 117 | 5650% | 5850% |
<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch (in order) | SpriteBatch (sorted by texture) | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Textures 0 | 0.000 | 0.000 | 0.100 | 0.400 |
| Textures 300 | 2.200 | 0.200 | 0.300 | 0.500 |
| Textures 3k | 21.600 | 1.400 | 1.000 | 1.000 |
| Textures 30k | 247.300 | 7.400 | 8.100 | 8.200 |

</details>

<details><summary>Median / 95th percentile frame time (ms)</summary>

| Scene | SpriteBatch (in order) | SpriteBatch (sorted by texture) | Skia render target | Skia direct |
|---|---:|---:|---:|---:|
| Textures 0 | 0.200 / 0.400 | 0.200 / 0.400 | 0.300 / 0.500 | 0.600 / 0.800 |
| Textures 300 | 2.300 / 2.900 | 0.300 / 0.600 | 0.400 / 0.800 | 0.700 / 1.000 |
| Textures 3k | 21.900 / 61.100 | 1.700 / 3.000 | 1.200 / 1.800 | 1.100 / 1.700 |
| Textures 30k | 248.000 / 767.000 | 7.800 / 15.600 | 8.400 / 26.600 | 8.500 / 9.800 |

</details>

<!-- results:end -->

## Reading the numbers

- `SpriteBatch` in submission order collapses as the count grows (about 200 FPS at 3k sprites on KNI DesktopGL, 21 FPS at 30k). Sorting by texture recovers most of it.
- Skia's per-texture `DrawAtlas` sits between the two: faster than unsorted `SpriteBatch` from about 3k sprites, slower than sorted `SpriteBatch`.
- The browser widens the gap: unsorted `SpriteBatch` runs at 40 FPS at 3k sprites and 2 FPS at 30k, where Skia holds 800+ and 110+.
