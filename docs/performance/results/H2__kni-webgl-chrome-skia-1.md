# KNI WebGL (Chrome) Skia

- Date: 2026-10-23
- Commit: 1df31c3
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: ANGLE (Intel, Intel(R) Arc(TM) Graphics (0x00007D55) Direct3D11 vs_5_0 ps_5_0, D3D11)
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | skia | Skia render target | 3344 | 0.300 | 0.500 | 0.100 |  |
| sprites-single | Sprites 0 | 0 | skia | Skia direct | 1689 | 0.600 | 0.800 | 0.400 |  |
| sprites-single | Sprites 500 | 500 | skia | Skia render target | 2050 | 0.400 | 0.900 | 0.300 |  |
| sprites-single | Sprites 500 | 500 | skia | Skia direct | 1393 | 0.700 | 1.000 | 0.600 |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia render target | 1063 | 0.900 | 1.700 | 0.600 |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia direct | 1064 | 0.900 | 1.300 | 0.800 |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia render target | 322 | 2.300 | 6.200 | 2.000 |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia direct | 322 | 3.000 | 3.900 | 2.900 |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia render target | 69 | 8.300 | 35.900 | 8.000 |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia direct | 72 | 13.800 | 17.800 | 13.400 |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia render target | 340 | 1.400 | 6.400 | 1.200 |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia direct | 348 | 2.800 | 3.700 | 2.600 |  |
| sprites-textures | Textures 0 | 0 | skia | Skia render target | 3210 | 0.300 | 0.500 | 0.100 |  |
| sprites-textures | Textures 0 | 0 | skia | Skia direct | 1677 | 0.600 | 0.800 | 0.400 |  |
| sprites-textures | Textures 300 | 300 | skia | Skia render target | 2268 | 0.400 | 0.800 | 0.300 |  |
| sprites-textures | Textures 300 | 300 | skia | Skia direct | 1434 | 0.700 | 1.000 | 0.500 |  |
| sprites-textures | Textures 3k | 3000 | skia | Skia render target | 825 | 1.200 | 1.800 | 1.000 |  |
| sprites-textures | Textures 3k | 3000 | skia | Skia direct | 848 | 1.100 | 1.700 | 1.000 |  |
| sprites-textures | Textures 30k | 30000 | skia | Skia render target | 113 | 8.400 | 26.600 | 8.100 |  |
| sprites-textures | Textures 30k | 30000 | skia | Skia direct | 117 | 8.500 | 9.800 | 8.200 |  |
| shapes | Circles 500 | 500 | skia | Skia render target | 1387 | 0.700 | 1.100 | 0.500 |  |
| shapes | Circles 500 | 500 | skia | Skia direct | 804 | 1.200 | 1.600 | 1.100 |  |
| shapes | Circles 2k | 2000 | skia | Skia render target | 475 | 2.000 | 2.900 | 1.900 |  |
| shapes | Circles 2k | 2000 | skia | Skia direct | 323 | 3.000 | 3.700 | 2.800 |  |
| shapes | Circles 10k | 10000 | skia | Skia render target | 112 | 8.700 | 12.000 | 8.400 |  |
| shapes | Circles 10k | 10000 | skia | Skia direct | 82 | 12.000 | 13.300 | 11.700 |  |
| shapes | Mixed shapes 2k | 2000 | skia | Skia render target | 88 | 11.200 | 12.700 | 10.900 |  |
| shapes | Mixed shapes 2k | 2000 | skia | Skia direct | 59 | 16.800 | 18.800 | 16.400 |  |
| shapes | Mixed shapes 10k | 10000 | skia | Skia render target | 18 | 54.500 | 57.500 | 54.000 |  |
| shapes | Mixed shapes 10k | 10000 | skia | Skia direct | 13 | 78.400 | 83.300 | 77.800 |  |
| gradients | Gradients 500 | 500 | skia | Skia render target | 339 | 2.900 | 3.800 | 2.800 |  |
| gradients | Gradients 500 | 500 | skia | Skia direct | 316 | 3.100 | 3.600 | 3.000 |  |
| gradients | Gradients 2k | 2000 | skia | Skia render target | 109 | 9.200 | 10.500 | 8.900 |  |
| gradients | Gradients 2k | 2000 | skia | Skia direct | 99 | 10.000 | 11.600 | 9.800 |  |
| gradients | Gradients 10k | 10000 | skia | Skia render target | 23 | 43.800 | 46.300 | 43.400 |  |
| gradients | Gradients 10k | 10000 | skia | Skia direct | 22 | 44.300 | 47.400 | 43.900 |  |
