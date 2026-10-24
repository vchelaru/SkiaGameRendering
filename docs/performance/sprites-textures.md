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

### MonoGame DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Textures 0 | 2698 (SpriteBatch (sorted by texture)) | 1668 | 62% |
| Textures 300 | 1447 (SpriteBatch (in order)) | 1010 | 70% |
| Textures 3k | 606 (SpriteBatch (sorted by texture)) | 491 | 81% |
| Textures 30k | 110 (SpriteBatch (sorted by texture)) | 65 | 59% |

### KNI DesktopGL, H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Textures 0 | 3062 (SpriteBatch (sorted by texture)) | 1695 | 55% |
| Textures 300 | 2111 (SpriteBatch (sorted by texture)) | 1027 | 49% |
| Textures 3k | 949 (SpriteBatch (sorted by texture)) | 480 | 51% |
| Textures 30k | 124 (SpriteBatch (sorted by texture)) | 58 | 47% |

### KNI WebGL (Chrome), H2

| Scene | Native FPS | SkiaGameRendering FPS | SkiaGameRendering / Native |
|---|---:|---:|---:|
| Textures 0 | 2806 (SpriteBatch (sorted by texture)) | 1677 | 60% |
| Textures 300 | 3069 (SpriteBatch (sorted by texture)) | 1434 | 47% |
| Textures 3k | 534 (SpriteBatch (sorted by texture)) | 848 | 159% |
| Textures 30k | 111 (SpriteBatch (sorted by texture)) | 117 | 105% |

<!-- results:end -->

## Reading the numbers

- Native is `SpriteBatch` sorted by texture, its best case. Skia, one `DrawAtlas` per texture, runs at 47-81% of it on the desktop.
- Browser: 159% at 3k sprites, 105% at 30k.
- `SpriteBatch` in submission order (not in the table) collapses with many texture changes: 21 FPS at 30k sprites on KNI DesktopGL, 2 FPS in the browser.
