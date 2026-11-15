# KNI WebGL (Chrome) Skia

- Date: 2026-10-23
- Commit: 1df31c3
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: ANGLE (Intel, Intel(R) Arc(TM) Graphics (0x00007D55) Direct3D11 vs_5_0 ps_5_0, D3D11)
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | Mean frame ms | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | skia | Skia render target | 0.299 | 0.300 | 0.500 | 0.100 |  |
| sprites-single | Sprites 0 | 0 | skia | Skia direct | 0.592 | 0.600 | 0.800 | 0.400 |  |
| sprites-single | Sprites 500 | 500 | skia | Skia render target | 0.488 | 0.400 | 0.900 | 0.300 |  |
| sprites-single | Sprites 500 | 500 | skia | Skia direct | 0.718 | 0.700 | 1.000 | 0.600 |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia render target | 0.941 | 0.900 | 1.700 | 0.600 |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia direct | 0.940 | 0.900 | 1.300 | 0.800 |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia render target | 3.106 | 2.300 | 6.200 | 2.000 |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia direct | 3.106 | 3.000 | 3.900 | 2.900 |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia render target | 14.493 | 8.300 | 35.900 | 8.000 |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia direct | 13.889 | 13.800 | 17.800 | 13.400 |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia render target | 2.941 | 1.400 | 6.400 | 1.200 |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia direct | 2.874 | 2.800 | 3.700 | 2.600 |  |
| sprites-textures | Textures 0 | 0 | skia | Skia render target | 0.312 | 0.300 | 0.500 | 0.100 |  |
| sprites-textures | Textures 0 | 0 | skia | Skia direct | 0.596 | 0.600 | 0.800 | 0.400 |  |
| sprites-textures | Textures 300 | 300 | skia | Skia render target | 0.441 | 0.400 | 0.800 | 0.300 |  |
| sprites-textures | Textures 300 | 300 | skia | Skia direct | 0.697 | 0.700 | 1.000 | 0.500 |  |
| sprites-textures | Textures 3k | 3000 | skia | Skia render target | 1.212 | 1.200 | 1.800 | 1.000 |  |
| sprites-textures | Textures 3k | 3000 | skia | Skia direct | 1.179 | 1.100 | 1.700 | 1.000 |  |
| sprites-textures | Textures 30k | 30000 | skia | Skia render target | 8.850 | 8.400 | 26.600 | 8.100 |  |
| sprites-textures | Textures 30k | 30000 | skia | Skia direct | 8.547 | 8.500 | 9.800 | 8.200 |  |
| shapes | Circles 500 | 500 | skia | Skia render target | 0.721 | 0.700 | 1.100 | 0.500 |  |
| shapes | Circles 500 | 500 | skia | Skia direct | 1.244 | 1.200 | 1.600 | 1.100 |  |
| shapes | Circles 2k | 2000 | skia | Skia render target | 2.105 | 2.000 | 2.900 | 1.900 |  |
| shapes | Circles 2k | 2000 | skia | Skia direct | 3.096 | 3.000 | 3.700 | 2.800 |  |
| shapes | Circles 10k | 10000 | skia | Skia render target | 8.929 | 8.700 | 12.000 | 8.400 |  |
| shapes | Circles 10k | 10000 | skia | Skia direct | 12.195 | 12.000 | 13.300 | 11.700 |  |
| shapes | Mixed shapes 2k | 2000 | skia | Skia render target | 11.364 | 11.200 | 12.700 | 10.900 |  |
| shapes | Mixed shapes 2k | 2000 | skia | Skia direct | 16.949 | 16.800 | 18.800 | 16.400 |  |
| shapes | Mixed shapes 10k | 10000 | skia | Skia render target | 55.556 | 54.500 | 57.500 | 54.000 |  |
| shapes | Mixed shapes 10k | 10000 | skia | Skia direct | 76.923 | 78.400 | 83.300 | 77.800 |  |
| gradients | Gradients 500 | 500 | skia | Skia render target | 2.950 | 2.900 | 3.800 | 2.800 |  |
| gradients | Gradients 500 | 500 | skia | Skia direct | 3.165 | 3.100 | 3.600 | 3.000 |  |
| gradients | Gradients 2k | 2000 | skia | Skia render target | 9.174 | 9.200 | 10.500 | 8.900 |  |
| gradients | Gradients 2k | 2000 | skia | Skia direct | 10.101 | 10.000 | 11.600 | 9.800 |  |
| gradients | Gradients 10k | 10000 | skia | Skia render target | 43.478 | 43.800 | 46.300 | 43.400 |  |
| gradients | Gradients 10k | 10000 | skia | Skia direct | 45.455 | 44.300 | 47.400 | 43.900 |  |
