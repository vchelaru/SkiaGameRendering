# MonoGame DesktopGL Skia

- Date: 2026-09-30
- Commit: 59d06ca
- OS: Windows 11 (26200)
- CPU: AMD Ryzen 7 260 (16 threads)
- GPU: NVIDIA GeForce RTX 5060 Laptop GPU
- Power: Plugged in
- Window: 1600x900, vsync off

| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | skia | Skia render target | 1830 |  |  |  |  |
| sprites-single | Sprites 500 | 500 | skia | Skia render target | 1440 |  |  |  |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia render target | 1327 |  |  |  |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia render target | 573 |  |  |  |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia render target | 159 |  |  |  |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia render target | 569 |  |  |  |  |
