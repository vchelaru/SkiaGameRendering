# KNI WebGL (Chrome) Skia

- Date: 2026-10-24
- Commit: 1df31c3
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: ANGLE (Intel, Intel(R) Arc(TM) Graphics (0x00007D55) Direct3D11 vs_5_0 ps_5_0, D3D11)
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | Mean frame ms | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| shadows | Shadows 100 | 100 | skia | Skia render target | 25.641 | 24.600 | 35.300 | 24.000 |  |
| shadows | Shadows 100 | 100 | skia | Skia direct | 45.455 | 44.700 | 54.200 | 44.300 |  |
| shadows | Shadows 500 | 500 | skia | Skia render target | 333.333 | 306.400 | 328.800 | 305.500 |  |
| shadows | Shadows 500 | 500 | skia | Skia direct | 333.333 | 364.700 | 419.500 | 364.100 |  |
| shadows | Shadows 2k | 2000 | skia | Skia render target | 1000.000 | 1514.500 | 1652.500 | 1513.600 |  |
| shadows | Shadows 2k | 2000 | skia | Skia direct | 1000.000 | 1613.700 | 1662.500 | 1612.800 |  |
| shadows | Shadows 500 unique sizes | 500 | skia | Skia render target | 500.000 | 438.800 | 578.000 | 438.400 |  |
| shadows | Shadows 500 unique sizes | 500 | skia | Skia direct | 500.000 | 411.200 | 600.100 | 410.400 |  |
| text | Text 50 | 50 | skia | Skia render target | 0.781 | 0.700 | 1.100 | 0.600 |  |
| text | Text 50 | 50 | skia | Skia direct | 0.924 | 0.900 | 1.300 | 0.700 |  |
| text | Text 200 | 200 | skia | Skia render target | 1.477 | 1.400 | 1.800 | 1.300 |  |
| text | Text 200 | 200 | skia | Skia direct | 1.637 | 1.600 | 2.000 | 1.400 |  |
| text | Text 1k | 1000 | skia | Skia render target | 5.747 | 5.500 | 6.900 | 5.300 |  |
| text | Text 1k | 1000 | skia | Skia direct | 6.173 | 6.000 | 7.500 | 5.700 |  |
| text-mixed | Mixed text 50 | 50 | skia | Skia render target | 0.805 | 0.700 | 1.300 | 0.600 |  |
| text-mixed | Mixed text 50 | 50 | skia | Skia direct | 0.887 | 0.800 | 1.300 | 0.700 |  |
| text-mixed | Mixed text 200 | 200 | skia | Skia render target | 1.445 | 1.400 | 1.800 | 1.300 |  |
| text-mixed | Mixed text 200 | 200 | skia | Skia direct | 1.626 | 1.600 | 2.000 | 1.400 |  |
| text-mixed | Mixed text 1k | 1000 | skia | Skia render target | 5.495 | 5.400 | 6.300 | 5.200 |  |
| text-mixed | Mixed text 1k | 1000 | skia | Skia direct | 6.369 | 6.100 | 8.000 | 5.800 |  |
| svg | SVG 100 | 100 | skia | Skia render target | 1.934 | 1.700 | 3.000 | 1.600 |  |
| svg | SVG 100 | 100 | skia | Skia direct | 2.825 | 2.800 | 3.300 | 2.600 |  |
| svg | SVG 100 | 100 | skia | Skia render target (pre-rasterized SVG) | 0.390 | 0.300 | 0.700 | 0.200 |  |
| svg | SVG 100 | 100 | skia | Skia direct (pre-rasterized SVG) | 0.679 | 0.700 | 0.900 | 0.500 |  |
| svg | SVG 500 | 500 | skia | Skia render target | 9.434 | 8.100 | 15.700 | 7.800 |  |
| svg | SVG 500 | 500 | skia | Skia direct | 13.699 | 12.800 | 19.200 | 12.500 |  |
| svg | SVG 500 | 500 | skia | Skia render target (pre-rasterized SVG) | 0.620 | 0.500 | 1.200 | 0.300 |  |
| svg | SVG 500 | 500 | skia | Skia direct (pre-rasterized SVG) | 0.778 | 0.700 | 1.100 | 0.600 |  |
| svg | SVG 2k | 2000 | skia | Skia render target | 28.571 | 28.500 | 30.000 | 28.000 |  |
| svg | SVG 2k | 2000 | skia | Skia direct | 40.000 | 40.600 | 43.200 | 40.100 |  |
| svg | SVG 2k | 2000 | skia | Skia render target (pre-rasterized SVG) | 1.259 | 1.000 | 2.500 | 0.700 |  |
| svg | SVG 2k | 2000 | skia | Skia direct (pre-rasterized SVG) | 1.410 | 1.400 | 1.900 | 1.200 |  |
| fill-rate | Opaque 10 layers | 10 | skia | Skia render target | 0.626 | 0.500 | 1.400 | 0.300 |  |
| fill-rate | Opaque 10 layers | 10 | skia | Skia direct | 0.852 | 0.800 | 1.200 | 0.700 |  |
| fill-rate | Opaque 40 layers | 40 | skia | Skia render target | 1.256 | 1.100 | 2.900 | 0.800 |  |
| fill-rate | Opaque 40 layers | 40 | skia | Skia direct | 1.393 | 1.300 | 1.900 | 1.100 |  |
| fill-rate | Translucent 10 layers | 10 | skia | Skia render target | 0.704 | 0.600 | 1.400 | 0.300 |  |
| fill-rate | Translucent 10 layers | 10 | skia | Skia direct | 0.877 | 0.800 | 1.200 | 0.700 |  |
| fill-rate | Translucent 40 layers | 40 | skia | Skia render target | 1.873 | 1.500 | 3.900 | 0.900 |  |
| fill-rate | Translucent 40 layers | 40 | skia | Skia direct | 1.555 | 1.500 | 2.100 | 1.300 |  |
| ui | UI 50 | 50 | skia | Skia render target | 1.111 | 1.000 | 1.600 | 0.900 |  |
| ui | UI 50 | 50 | skia | Skia direct | 1.460 | 1.400 | 1.800 | 1.300 |  |
| ui | UI 200 | 200 | skia | Skia render target | 2.525 | 2.500 | 3.100 | 2.300 |  |
| ui | UI 200 | 200 | skia | Skia direct | 3.774 | 3.700 | 4.500 | 3.500 |  |
| ui | UI 1k | 1000 | skia | Skia render target | 10.526 | 10.400 | 11.400 | 10.100 |  |
| ui | UI 1k | 1000 | skia | Skia direct | 14.085 | 14.000 | 15.900 | 13.600 |  |
