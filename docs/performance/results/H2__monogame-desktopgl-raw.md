# MonoGame DesktopGL Raw

- Date: 2026-10-23
- Commit: 2a3a7c6
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | engine | SpriteBatch | 1680 | 0.496 | 1.069 | 0.009 |  |
| sprites-single | Sprites 500 | 500 | engine | SpriteBatch | 1433 | 0.594 | 1.454 | 0.077 |  |
| sprites-single | Sprites 2k | 2000 | engine | SpriteBatch | 1021 | 0.862 | 2.273 | 0.157 |  |
| sprites-single | Sprites 10k | 10000 | engine | SpriteBatch | 270 | 3.306 | 5.561 | 0.762 |  |
| sprites-single | Sprites 50k | 50000 | engine | SpriteBatch | 62 | 16.185 | 18.856 | 3.651 |  |
| sprites-single | Tinted sprites 10k | 10000 | engine | SpriteBatch | 325 | 2.784 | 4.714 | 0.765 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (in order) | 2523 | 0.344 | 0.519 | 0.006 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (sorted by texture) | 2698 | 0.329 | 0.464 | 0.006 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (in order) | 1447 | 0.661 | 0.916 | 0.538 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (sorted by texture) | 1438 | 0.605 | 1.479 | 0.095 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (in order) | 172 | 5.581 | 7.296 | 5.348 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (sorted by texture) | 606 | 1.470 | 3.385 | 0.390 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (in order) | 20 | 49.465 | 52.633 | 49.089 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (sorted by texture) | 110 | 9.221 | 10.813 | 6.880 |  |
| shapes | Circles 500 | 500 | engine | Apos.Shapes | 1017 | 0.890 | 2.341 | 0.097 |  |
| shapes | Circles 2k | 2000 | engine | Apos.Shapes | 562 | 1.581 | 3.156 | 0.241 |  |
| shapes | Circles 10k | 10000 | engine | Apos.Shapes | 217 | 4.273 | 6.081 | 2.711 |  |
| shapes | Mixed shapes 2k | 2000 | engine | Apos.Shapes | 543 | 1.653 | 3.207 | 0.359 |  |
| shapes | Mixed shapes 10k | 10000 | engine | Apos.Shapes | 146 | 6.439 | 9.213 | 3.334 |  |
| gradients | Gradients 500 | 500 | engine | Apos.Shapes | 886 | 0.989 | 2.533 | 0.134 |  |
| gradients | Gradients 2k | 2000 | engine | Apos.Shapes | 552 | 1.632 | 3.161 | 0.346 |  |
| gradients | Gradients 10k | 10000 | engine | Apos.Shapes | 167 | 5.492 | 7.512 | 4.170 |  |
| shadows | Shadows 100 | 100 | engine | Apos.Shapes | 758 | 1.192 | 2.557 | 0.065 |  |
| shadows | Shadows 500 | 500 | engine | Apos.Shapes | 216 | 4.182 | 6.027 | 0.934 |  |
| shadows | Shadows 2k | 2000 | engine | Apos.Shapes | 59 | 17.073 | 19.002 | 2.992 |  |
| shadows | Shadows 500 unique sizes | 500 | engine | Apos.Shapes | 206 | 4.533 | 6.502 | 0.954 |  |
| text | Text 50 | 50 | engine | SpriteFont | 1679 | 0.519 | 1.049 | 0.067 |  |
| text | Text 50 | 50 | engine | FontStashSharp | 1635 | 0.532 | 0.761 | 0.066 |  |
| text | Text 200 | 200 | engine | SpriteFont | 1630 | 0.529 | 0.947 | 0.134 |  |
| text | Text 200 | 200 | engine | FontStashSharp | 1636 | 0.530 | 0.973 | 0.185 |  |
| text | Text 1k | 1000 | engine | SpriteFont | 1093 | 0.839 | 1.342 | 0.650 |  |
| text | Text 1k | 1000 | engine | FontStashSharp | 858 | 1.111 | 1.460 | 0.939 |  |
| text-mixed | Mixed text 50 | 50 | engine | SpriteFont | 1590 | 0.546 | 0.853 | 0.133 |  |
| text-mixed | Mixed text 50 | 50 | engine | FontStashSharp | 1606 | 0.543 | 0.797 | 0.066 |  |
| text-mixed | Mixed text 200 | 200 | engine | SpriteFont | 1282 | 0.678 | 1.365 | 0.499 |  |
| text-mixed | Mixed text 200 | 200 | engine | FontStashSharp | 1381 | 0.619 | 1.717 | 0.229 |  |
| text-mixed | Mixed text 1k | 1000 | engine | SpriteFont | 417 | 2.337 | 2.619 | 2.164 |  |
| text-mixed | Mixed text 1k | 1000 | engine | FontStashSharp | 810 | 1.116 | 2.058 | 0.950 |  |
| svg | SVG 100 | 100 | engine | Apos.Shapes SVG | 655 | 1.376 | 2.847 | 0.829 |  |
| svg | SVG 100 | 100 | engine | SpriteBatch (pre-rasterized SVG) | 2080 | 0.425 | 0.653 | 0.029 |  |
| svg | SVG 500 | 500 | engine | Apos.Shapes SVG | 144 | 6.757 | 8.787 | 5.709 |  |
| svg | SVG 500 | 500 | engine | SpriteBatch (pre-rasterized SVG) | 1038 | 0.853 | 2.385 | 0.082 |  |
| svg | SVG 2k | 2000 | engine | Apos.Shapes SVG | 38 | 23.257 | 33.410 | 22.669 |  |
| svg | SVG 2k | 2000 | engine | SpriteBatch (pre-rasterized SVG) | 589 | 1.471 | 3.165 | 0.167 |  |
| fill-rate | Opaque 10 layers | 10 | engine | SpriteBatch | 1321 | 0.679 | 1.332 | 0.028 |  |
| fill-rate | Opaque 10 layers | 10 | engine | Apos.Shapes | 152 | 1.932 | 20.545 | 0.112 |  |
| fill-rate | Opaque 40 layers | 40 | engine | SpriteBatch | 684 | 1.305 | 2.774 | 0.054 |  |
| fill-rate | Opaque 40 layers | 40 | engine | Apos.Shapes | 43 | 4.515 | 87.247 | 0.200 |  |
| fill-rate | Translucent 10 layers | 10 | engine | SpriteBatch | 1259 | 0.714 | 1.416 | 0.039 |  |
| fill-rate | Translucent 10 layers | 10 | engine | Apos.Shapes | 139 | 1.716 | 27.099 | 0.114 |  |
| fill-rate | Translucent 40 layers | 40 | engine | SpriteBatch | 661 | 1.367 | 2.768 | 0.053 |  |
| fill-rate | Translucent 40 layers | 40 | engine | Apos.Shapes | 45 | 19.818 | 86.524 | 0.203 |  |
| ui | UI 50 | 50 | engine | Apos.Shapes + SpriteBatch | 319 | 1.070 | 11.359 | 0.310 |  |
| ui | UI 200 | 200 | engine | Apos.Shapes + SpriteBatch | 215 | 1.781 | 17.616 | 0.894 |  |
| ui | UI 1k | 1000 | engine | Apos.Shapes + SpriteBatch | 40 | 7.024 | 100.129 | 4.820 |  |
