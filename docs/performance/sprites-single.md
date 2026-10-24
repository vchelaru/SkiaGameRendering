# Sprites, one texture

One 64x64 soft-edged disc, drawn thousands of times with scale, rotation and (in the last scene) a per-sprite tint. This is the case `SpriteBatch` is built for, so it shows the cost of going through Skia when you do not need to.

- **Engine side:** `SpriteBatch.Draw` in one deferred batch.
- **Skia side:** One `DrawAtlas` call on a GPU-resident image, tinted through the `colors` array with `SKBlendMode.Modulate`.

| Engine | Skia (direct) |
|---|---|
| ![Sprites, one texture, engine](images/sprites-single-engine.jpg) | ![Sprites, one texture, Skia](images/sprites-single-skia.jpg) |

Screenshots: 2k sprites, KNI DesktopGL. The scenes are 1600x900 and animate every frame.

## Results

<!-- results:begin -->

### MonoGame DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Sprites 0 | 1680 (SpriteBatch) | 1634 | 97% |
| Sprites 500 | 1433 (SpriteBatch) | 941 | 66% |
| Sprites 2k | 1021 (SpriteBatch) | 600 | 59% |
| Sprites 10k | 270 (SpriteBatch) | 270 | 100% |
| Sprites 50k | 62 (SpriteBatch) | 52 | 84% |
| Tinted sprites 10k | 325 (SpriteBatch) | 267 | 82% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Sprites 0 | 2132 (SpriteBatch) | 1741 | 82% |
| Sprites 500 | 1945 (SpriteBatch) | 949 | 49% |
| Sprites 2k | 1335 (SpriteBatch) | 633 | 47% |
| Sprites 10k | 364 (SpriteBatch) | 244 | 67% |
| Sprites 50k | 76 (SpriteBatch) | 52 | 68% |
| Tinted sprites 10k | 399 (SpriteBatch) | 272 | 68% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Sprites 0 | 2759 (SpriteBatch) | 1689 | 61% |
| Sprites 500 | 1522 (SpriteBatch) | 1393 | 92% |
| Sprites 2k | 1025 (SpriteBatch) | 1064 | 104% |
| Sprites 10k | 241 (SpriteBatch) | 322 | 134% |
| Sprites 50k | 66 (SpriteBatch) | 72 | 109% |
| Tinted sprites 10k | 379 (SpriteBatch) | 348 | 92% |

<!-- results:end -->

## Reading the numbers

- Desktop: Skia runs at 47-100% of `SpriteBatch`, lowest at 500-2k sprites, where Skia's fixed cost per frame (context switch and flush, about 0.1 ms) is a large share of a short frame.
- Browser: roughly even from 500 sprites up (92-134%).
- Tinting through `DrawAtlas`'s `colors` costs almost nothing.

## Faster: one `DrawAtlas`, not one call per sprite

MonoGame DesktopGL, `SkiaDirectCanvas`, 1600x900, H1 (Ryzen 7 260, RTX 5060 Laptop, driver 32.0.16.1047). Draw CPU time per frame in ms, median [min-max] of 6 runs (`--quick` windows), same scene and sprites in every row.

| Scene | `DrawAtlas` (one call) | `DrawImageRect` per sprite | `DrawImage` per sprite | per-sprite / `DrawAtlas` |
|---|---:|---:|---:|---:|
| Sprites 500 | 0.22 [0.19-0.36] | 0.61 [0.57-0.66] | 0.59 [0.56-0.67] | 2.7x |
| Sprites 2k | 0.29 [0.26-0.40] | 2.16 [1.79-3.24] | 1.92 [1.76-2.25] | 6.5x-7.4x |
| Sprites 10k | 1.05 [0.61-1.44] | 10.1 [9.1-14.7] | 10.1 [9.2-16.3] | 9.6x |
| Sprites 50k | 4.0 [3.9-4.6] | 49 [45-80] | 47 [46-60] | 11.7x-12.2x |
| Tinted sprites 10k | 0.94 [0.85-1.04] | 10.0 [8.9-14.6] | 10.7 [9.0-13.7] | 10.7x-11.4x |

Each per-sprite call sets the canvas matrix to the sprite's rotation and scale first. Use `DrawAtlas` with one `SKRotationScaleMatrix` per sprite, as the Skia scene here does.

Tried and no gain beyond the noise on the `DrawAtlas` path:

- `SKFilterMode.Nearest` instead of `Linear`: 0.27 vs 0.29 ms at 2k, 0.97 vs 1.05 ms at 10k, but the runs overlap (2k `DrawAtlas` ranged 0.26-0.40), and 50k and tinted 10k went the other way.
- Passing no `SKPaint` (`null`) instead of a reused one: within 5% at every count, runs overlap.

Output is not claimed to be identical between the atlas and per-sprite paths, so the speedup applies to sprites you would draw with the same filtering either way.
