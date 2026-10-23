# Sprites, one texture

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

<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch | Skia render target |
|---|---:|---:|
| Sprites 0 |  |  |
| Sprites 500 |  |  |
| Sprites 2k |  |  |
| Sprites 10k |  |  |
| Sprites 50k |  |  |
| Tinted sprites 10k |  |  |

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

<details><summary>Median draw CPU time per frame (ms)</summary>

| Scene | SpriteBatch | Skia render target |
|---|---:|---:|
| Sprites 0 |  |  |
| Sprites 500 |  |  |
| Sprites 2k |  |  |
| Sprites 10k |  |  |
| Sprites 50k |  |  |
| Tinted sprites 10k |  |  |

</details>

<!-- results:end -->
