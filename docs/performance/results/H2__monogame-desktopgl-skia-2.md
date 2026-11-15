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
| shadows | Shadows 100 | 100 | skia | Skia render target | 9.009 | 8.905 | 10.317 | 5.852 |  |
| shadows | Shadows 100 | 100 | skia | Skia direct | 8.621 | 8.479 | 10.211 | 5.676 |  |
| shadows | Shadows 500 | 500 | skia | Skia render target | 35.714 | 35.245 | 37.862 | 28.408 |  |
| shadows | Shadows 500 | 500 | skia | Skia direct | 35.714 | 35.219 | 37.967 | 28.079 |  |
| shadows | Shadows 2k | 2000 | skia | Skia render target | 166.667 | 168.722 | 174.632 | 129.422 |  |
| shadows | Shadows 2k | 2000 | skia | Skia direct | 166.667 | 167.473 | 178.928 | 127.652 |  |
| shadows | Shadows 500 unique sizes | 500 | skia | Skia render target | 38.462 | 39.164 | 41.706 | 31.515 |  |
| shadows | Shadows 500 unique sizes | 500 | skia | Skia direct | 37.037 | 37.044 | 40.984 | 30.433 |  |
| text | Text 50 | 50 | skia | Skia render target | 1.127 | 1.108 | 1.404 | 0.337 |  |
| text | Text 50 | 50 | skia | Skia direct | 0.975 | 0.936 | 1.287 | 0.321 |  |
| text | Text 200 | 200 | skia | Skia render target | 1.832 | 1.711 | 2.576 | 0.788 |  |
| text | Text 200 | 200 | skia | Skia direct | 1.486 | 1.453 | 1.805 | 0.726 |  |
| text | Text 1k | 1000 | skia | Skia render target | 5.848 | 5.453 | 8.447 | 3.044 |  |
| text | Text 1k | 1000 | skia | Skia direct | 5.025 | 4.754 | 6.818 | 2.963 |  |
| text-mixed | Mixed text 50 | 50 | skia | Skia render target | 1.110 | 1.065 | 1.439 | 0.342 |  |
| text-mixed | Mixed text 50 | 50 | skia | Skia direct | 0.981 | 0.956 | 1.263 | 0.313 |  |
| text-mixed | Mixed text 200 | 200 | skia | Skia render target | 2.000 | 1.862 | 2.773 | 0.793 |  |
| text-mixed | Mixed text 200 | 200 | skia | Skia direct | 1.449 | 1.386 | 1.808 | 0.730 |  |
| text-mixed | Mixed text 1k | 1000 | skia | Skia render target | 6.452 | 6.110 | 8.826 | 3.100 |  |
| text-mixed | Mixed text 1k | 1000 | skia | Skia direct | 6.098 | 5.681 | 8.674 | 3.084 |  |
| svg | SVG 100 | 100 | skia | Skia render target | 15.873 | 15.815 | 17.331 | 13.367 |  |
| svg | SVG 100 | 100 | skia | Skia direct | 13.514 | 13.334 | 15.658 | 10.321 |  |
| svg | SVG 100 | 100 | skia | Skia render target (pre-rasterized SVG) | 0.959 | 0.932 | 1.301 | 0.187 |  |
| svg | SVG 100 | 100 | skia | Skia direct (pre-rasterized SVG) | 0.923 | 0.904 | 1.146 | 0.150 |  |
| svg | SVG 500 | 500 | skia | Skia render target | 52.632 | 53.120 | 56.747 | 46.884 |  |
| svg | SVG 500 | 500 | skia | Skia direct | 55.556 | 53.883 | 57.484 | 47.410 |  |
| svg | SVG 500 | 500 | skia | Skia render target (pre-rasterized SVG) | 1.248 | 1.225 | 1.496 | 0.201 |  |
| svg | SVG 500 | 500 | skia | Skia direct (pre-rasterized SVG) | 1.199 | 1.184 | 1.458 | 0.153 |  |
| svg | SVG 2k | 2000 | skia | Skia render target | 250.000 | 261.900 | 267.180 | 261.490 |  |
| svg | SVG 2k | 2000 | skia | Skia direct | 250.000 | 263.207 | 271.994 | 262.920 |  |
| svg | SVG 2k | 2000 | skia | Skia render target (pre-rasterized SVG) | 2.475 | 2.418 | 2.890 | 0.379 |  |
| svg | SVG 2k | 2000 | skia | Skia direct (pre-rasterized SVG) | 1.992 | 1.959 | 2.323 | 0.374 |  |
| fill-rate | Opaque 10 layers | 10 | skia | Skia render target | 1.560 | 1.534 | 1.852 | 0.206 |  |
| fill-rate | Opaque 10 layers | 10 | skia | Skia direct | 1.414 | 1.388 | 1.691 | 0.196 |  |
| fill-rate | Opaque 40 layers | 40 | skia | Skia render target | 2.252 | 2.163 | 2.568 | 0.305 |  |
| fill-rate | Opaque 40 layers | 40 | skia | Skia direct | 1.912 | 1.862 | 2.185 | 0.229 |  |
| fill-rate | Translucent 10 layers | 10 | skia | Skia render target | 1.464 | 1.424 | 1.822 | 0.165 |  |
| fill-rate | Translucent 10 layers | 10 | skia | Skia direct | 1.420 | 1.376 | 1.752 | 0.125 |  |
| fill-rate | Translucent 40 layers | 40 | skia | Skia render target | 2.004 | 1.968 | 2.284 | 0.254 |  |
| fill-rate | Translucent 40 layers | 40 | skia | Skia direct | 1.698 | 1.665 | 1.940 | 0.190 |  |
| ui | UI 50 | 50 | skia | Skia render target | 1.508 | 1.417 | 2.042 | 0.607 |  |
| ui | UI 50 | 50 | skia | Skia direct | 1.256 | 1.202 | 1.529 | 0.551 |  |
| ui | UI 200 | 200 | skia | Skia render target | 3.333 | 3.197 | 4.145 | 1.534 |  |
| ui | UI 200 | 200 | skia | Skia direct | 2.674 | 2.492 | 4.018 | 1.513 |  |
| ui | UI 1k | 1000 | skia | Skia render target | 12.987 | 12.708 | 16.882 | 6.771 |  |
| ui | UI 1k | 1000 | skia | Skia direct | 12.821 | 12.503 | 16.249 | 6.919 |  |
