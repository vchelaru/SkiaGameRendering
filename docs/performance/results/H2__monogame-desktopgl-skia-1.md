# MonoGame DesktopGL Skia

- Date: 2026-10-23
- Commit: 2a3a7c6
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | Mean frame ms | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | skia | Skia render target | 1.038 | 1.014 | 1.325 | 0.154 |  |
| sprites-single | Sprites 0 | 0 | skia | Skia direct | 0.612 | 0.588 | 0.801 | 0.094 |  |
| sprites-single | Sprites 500 | 500 | skia | Skia render target | 1.403 | 1.358 | 1.728 | 0.191 |  |
| sprites-single | Sprites 500 | 500 | skia | Skia direct | 1.063 | 1.029 | 1.363 | 0.145 |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia render target | 2.128 | 2.071 | 2.510 | 0.359 |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia direct | 1.667 | 1.585 | 2.089 | 0.279 |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia render target | 4.184 | 4.028 | 4.979 | 0.857 |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia direct | 3.704 | 3.571 | 4.515 | 0.709 |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia render target | 20.000 | 19.754 | 22.617 | 4.097 |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia direct | 19.231 | 18.987 | 22.365 | 4.338 |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia render target | 4.184 | 4.047 | 4.864 | 1.025 |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia direct | 3.745 | 3.633 | 4.458 | 0.945 |  |
| sprites-textures | Textures 0 | 0 | skia | Skia render target | 1.025 | 1.015 | 1.284 | 0.146 |  |
| sprites-textures | Textures 0 | 0 | skia | Skia direct | 0.600 | 0.576 | 0.801 | 0.085 |  |
| sprites-textures | Textures 300 | 300 | skia | Skia render target | 1.294 | 1.253 | 1.596 | 0.240 |  |
| sprites-textures | Textures 300 | 300 | skia | Skia direct | 0.990 | 0.959 | 1.227 | 0.192 |  |
| sprites-textures | Textures 3k | 3000 | skia | Skia render target | 2.688 | 2.666 | 3.051 | 0.537 |  |
| sprites-textures | Textures 3k | 3000 | skia | Skia direct | 2.037 | 1.962 | 2.419 | 0.345 |  |
| sprites-textures | Textures 30k | 30000 | skia | Skia render target | 15.625 | 15.456 | 16.555 | 2.616 |  |
| sprites-textures | Textures 30k | 30000 | skia | Skia direct | 15.385 | 15.493 | 16.747 | 2.811 |  |
| shapes | Circles 500 | 500 | skia | Skia render target | 1.522 | 1.432 | 2.006 | 0.437 |  |
| shapes | Circles 500 | 500 | skia | Skia direct | 1.220 | 1.172 | 1.537 | 0.390 |  |
| shapes | Circles 2k | 2000 | skia | Skia render target | 2.924 | 2.813 | 3.559 | 1.140 |  |
| shapes | Circles 2k | 2000 | skia | Skia direct | 2.320 | 2.219 | 2.949 | 1.085 |  |
| shapes | Circles 10k | 10000 | skia | Skia render target | 11.364 | 11.027 | 13.849 | 4.956 |  |
| shapes | Circles 10k | 10000 | skia | Skia direct | 9.901 | 9.879 | 12.943 | 4.757 |  |
| shapes | Mixed shapes 2k | 2000 | skia | Skia render target | 14.925 | 14.729 | 17.520 | 11.453 |  |
| shapes | Mixed shapes 2k | 2000 | skia | Skia direct | 14.493 | 14.369 | 16.599 | 11.644 |  |
| shapes | Mixed shapes 10k | 10000 | skia | Skia render target | 66.667 | 65.148 | 70.437 | 58.128 |  |
| shapes | Mixed shapes 10k | 10000 | skia | Skia direct | 66.667 | 66.757 | 70.799 | 60.868 |  |
| gradients | Gradients 500 | 500 | skia | Skia render target | 2.439 | 2.320 | 3.188 | 1.543 |  |
| gradients | Gradients 500 | 500 | skia | Skia direct | 2.252 | 2.134 | 3.037 | 1.502 |  |
| gradients | Gradients 2k | 2000 | skia | Skia render target | 8.475 | 8.278 | 9.801 | 5.780 |  |
| gradients | Gradients 2k | 2000 | skia | Skia direct | 8.130 | 7.822 | 10.125 | 5.666 |  |
| gradients | Gradients 10k | 10000 | skia | Skia render target | 35.714 | 36.092 | 39.153 | 30.702 |  |
| gradients | Gradients 10k | 10000 | skia | Skia direct | 35.714 | 36.059 | 38.714 | 31.233 |  |
