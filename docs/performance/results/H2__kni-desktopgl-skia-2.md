# KNI DesktopGL Skia

- Date: 2026-10-23
- Commit: 1df31c3
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| shadows | Shadows 100 | 100 | skia | Skia render target | 115 | 8.663 | 10.375 | 5.756 |  |
| shadows | Shadows 100 | 100 | skia | Skia direct | 106 | 9.217 | 11.893 | 5.973 |  |
| shadows | Shadows 500 | 500 | skia | Skia render target | 27 | 36.419 | 39.995 | 28.611 |  |
| shadows | Shadows 500 | 500 | skia | Skia direct | 28 | 35.396 | 38.347 | 28.058 |  |
| shadows | Shadows 2k | 2000 | skia | Skia render target | 6 | 167.362 | 173.059 | 128.697 |  |
| shadows | Shadows 2k | 2000 | skia | Skia direct | 6 | 167.829 | 175.913 | 130.280 |  |
| shadows | Shadows 500 unique sizes | 500 | skia | Skia render target | 26 | 37.900 | 40.240 | 30.920 |  |
| shadows | Shadows 500 unique sizes | 500 | skia | Skia direct | 27 | 36.497 | 39.694 | 29.702 |  |
| text | Text 50 | 50 | skia | Skia render target | 782 | 1.253 | 1.529 | 0.506 |  |
| text | Text 50 | 50 | skia | Skia direct | 1053 | 0.922 | 1.252 | 0.315 |  |
| text | Text 200 | 200 | skia | Skia render target | 530 | 1.836 | 2.288 | 0.934 |  |
| text | Text 200 | 200 | skia | Skia direct | 747 | 1.286 | 1.661 | 0.730 |  |
| text | Text 1k | 1000 | skia | Skia render target | 171 | 5.459 | 8.701 | 3.165 |  |
| text | Text 1k | 1000 | skia | Skia direct | 190 | 4.988 | 7.582 | 2.897 |  |
| text-mixed | Mixed text 50 | 50 | skia | Skia render target | 841 | 1.173 | 1.386 | 0.459 |  |
| text-mixed | Mixed text 50 | 50 | skia | Skia direct | 1001 | 0.969 | 1.301 | 0.324 |  |
| text-mixed | Mixed text 200 | 200 | skia | Skia render target | 503 | 1.940 | 2.375 | 0.927 |  |
| text-mixed | Mixed text 200 | 200 | skia | Skia direct | 674 | 1.367 | 2.200 | 0.749 |  |
| text-mixed | Mixed text 1k | 1000 | skia | Skia render target | 166 | 5.799 | 8.502 | 3.230 |  |
| text-mixed | Mixed text 1k | 1000 | skia | Skia direct | 167 | 5.618 | 8.484 | 3.024 |  |
| svg | SVG 100 | 100 | skia | Skia render target | 63 | 15.816 | 17.814 | 13.621 |  |
| svg | SVG 100 | 100 | skia | Skia direct | 73 | 13.336 | 15.911 | 10.384 |  |
| svg | SVG 100 | 100 | skia | Skia render target (pre-rasterized SVG) | 1053 | 0.929 | 1.213 | 0.310 |  |
| svg | SVG 100 | 100 | skia | Skia direct (pre-rasterized SVG) | 1158 | 0.864 | 1.091 | 0.145 |  |
| svg | SVG 500 | 500 | skia | Skia render target | 18 | 54.281 | 57.647 | 47.867 |  |
| svg | SVG 500 | 500 | skia | Skia direct | 18 | 54.331 | 60.112 | 47.657 |  |
| svg | SVG 500 | 500 | skia | Skia render target (pre-rasterized SVG) | 774 | 1.270 | 1.547 | 0.367 |  |
| svg | SVG 500 | 500 | skia | Skia direct (pre-rasterized SVG) | 912 | 1.019 | 1.450 | 0.156 |  |
| svg | SVG 2k | 2000 | skia | Skia render target | 4 | 263.541 | 267.238 | 263.136 |  |
| svg | SVG 2k | 2000 | skia | Skia direct | 4 | 262.411 | 269.328 | 261.971 |  |
| svg | SVG 2k | 2000 | skia | Skia render target (pre-rasterized SVG) | 339 | 2.889 | 3.292 | 0.619 |  |
| svg | SVG 2k | 2000 | skia | Skia direct (pre-rasterized SVG) | 502 | 1.956 | 2.297 | 0.332 |  |
| fill-rate | Opaque 10 layers | 10 | skia | Skia render target | 628 | 1.543 | 1.963 | 0.368 |  |
| fill-rate | Opaque 10 layers | 10 | skia | Skia direct | 722 | 1.365 | 1.661 | 0.182 |  |
| fill-rate | Opaque 40 layers | 40 | skia | Skia render target | 386 | 2.504 | 2.883 | 0.399 |  |
| fill-rate | Opaque 40 layers | 40 | skia | Skia direct | 557 | 1.751 | 2.052 | 0.160 |  |
| fill-rate | Translucent 10 layers | 10 | skia | Skia render target | 687 | 1.426 | 1.702 | 0.334 |  |
| fill-rate | Translucent 10 layers | 10 | skia | Skia direct | 756 | 1.309 | 1.585 | 0.148 |  |
| fill-rate | Translucent 40 layers | 40 | skia | Skia render target | 412 | 2.389 | 2.714 | 0.441 |  |
| fill-rate | Translucent 40 layers | 40 | skia | Skia direct | 476 | 2.055 | 2.380 | 0.296 |  |
| ui | UI 50 | 50 | skia | Skia render target | 592 | 1.680 | 2.099 | 0.741 |  |
| ui | UI 50 | 50 | skia | Skia direct | 723 | 1.344 | 1.746 | 0.559 |  |
| ui | UI 200 | 200 | skia | Skia render target | 288 | 3.349 | 4.384 | 1.723 |  |
| ui | UI 200 | 200 | skia | Skia direct | 374 | 2.511 | 3.889 | 1.504 |  |
| ui | UI 1k | 1000 | skia | Skia render target | 76 | 12.922 | 17.009 | 6.884 |  |
| ui | UI 1k | 1000 | skia | Skia direct | 71 | 13.681 | 17.226 | 7.566 |  |
