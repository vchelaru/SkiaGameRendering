# KNI DesktopGL Skia

- Date: 2026-10-23
- Commit: 1df31c3
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | Mean frame ms | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | skia | Skia render target | 1.017 | 0.993 | 1.337 | 0.283 |  |
| sprites-single | Sprites 0 | 0 | skia | Skia direct | 0.574 | 0.541 | 0.785 | 0.067 |  |
| sprites-single | Sprites 500 | 500 | skia | Skia render target | 1.406 | 1.367 | 1.722 | 0.364 |  |
| sprites-single | Sprites 500 | 500 | skia | Skia direct | 1.054 | 1.032 | 1.292 | 0.184 |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia render target | 2.169 | 2.098 | 2.524 | 0.453 |  |
| sprites-single | Sprites 2k | 2000 | skia | Skia direct | 1.580 | 1.530 | 1.879 | 0.291 |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia render target | 4.000 | 3.837 | 4.666 | 0.837 |  |
| sprites-single | Sprites 10k | 10000 | skia | Skia direct | 4.098 | 4.035 | 4.725 | 0.931 |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia render target | 19.608 | 19.804 | 22.059 | 5.875 |  |
| sprites-single | Sprites 50k | 50000 | skia | Skia direct | 19.231 | 18.977 | 21.865 | 4.945 |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia render target | 4.167 | 3.992 | 4.907 | 1.107 |  |
| sprites-single | Tinted sprites 10k | 10000 | skia | Skia direct | 3.676 | 3.542 | 4.361 | 0.943 |  |
| sprites-textures | Textures 0 | 0 | skia | Skia render target | 0.976 | 1.001 | 1.338 | 0.275 |  |
| sprites-textures | Textures 0 | 0 | skia | Skia direct | 0.590 | 0.565 | 0.767 | 0.087 |  |
| sprites-textures | Textures 300 | 300 | skia | Skia render target | 1.271 | 1.241 | 1.586 | 0.366 |  |
| sprites-textures | Textures 300 | 300 | skia | Skia direct | 0.974 | 0.945 | 1.177 | 0.183 |  |
| sprites-textures | Textures 3k | 3000 | skia | Skia render target | 2.717 | 2.621 | 3.047 | 0.564 |  |
| sprites-textures | Textures 3k | 3000 | skia | Skia direct | 2.083 | 1.996 | 2.445 | 0.383 |  |
| sprites-textures | Textures 30k | 30000 | skia | Skia render target | 17.857 | 17.862 | 18.764 | 3.580 |  |
| sprites-textures | Textures 30k | 30000 | skia | Skia direct | 17.241 | 17.322 | 18.300 | 3.243 |  |
| shapes | Circles 500 | 500 | skia | Skia render target | 1.443 | 1.441 | 1.792 | 0.551 |  |
| shapes | Circles 500 | 500 | skia | Skia direct | 1.199 | 1.159 | 1.600 | 0.397 |  |
| shapes | Circles 2k | 2000 | skia | Skia render target | 2.985 | 2.878 | 3.687 | 1.299 |  |
| shapes | Circles 2k | 2000 | skia | Skia direct | 2.165 | 2.073 | 2.781 | 1.110 |  |
| shapes | Circles 10k | 10000 | skia | Skia render target | 11.628 | 11.360 | 14.123 | 5.355 |  |
| shapes | Circles 10k | 10000 | skia | Skia direct | 10.989 | 10.806 | 13.908 | 5.496 |  |
| shapes | Mixed shapes 2k | 2000 | skia | Skia render target | 14.925 | 14.632 | 17.166 | 11.642 |  |
| shapes | Mixed shapes 2k | 2000 | skia | Skia direct | 14.286 | 14.088 | 16.650 | 11.531 |  |
| shapes | Mixed shapes 10k | 10000 | skia | Skia render target | 66.667 | 65.310 | 69.746 | 58.894 |  |
| shapes | Mixed shapes 10k | 10000 | skia | Skia direct | 66.667 | 65.103 | 69.524 | 58.619 |  |
| gradients | Gradients 500 | 500 | skia | Skia render target | 2.841 | 2.751 | 3.387 | 1.740 |  |
| gradients | Gradients 500 | 500 | skia | Skia direct | 2.439 | 2.305 | 3.294 | 1.509 |  |
| gradients | Gradients 2k | 2000 | skia | Skia render target | 8.333 | 8.202 | 9.483 | 5.811 |  |
| gradients | Gradients 2k | 2000 | skia | Skia direct | 7.937 | 7.592 | 9.792 | 5.575 |  |
| gradients | Gradients 10k | 10000 | skia | Skia render target | 37.037 | 37.188 | 40.125 | 32.612 |  |
| gradients | Gradients 10k | 10000 | skia | Skia direct | 37.037 | 36.857 | 40.476 | 32.380 |  |
