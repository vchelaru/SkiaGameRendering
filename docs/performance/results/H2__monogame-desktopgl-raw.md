# MonoGame DesktopGL Raw

- Date: 2026-10-23
- Commit: 2a3a7c6
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | Mean frame ms | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | engine | SpriteBatch | 0.595 | 0.496 | 1.069 | 0.009 |  |
| sprites-single | Sprites 500 | 500 | engine | SpriteBatch | 0.698 | 0.594 | 1.454 | 0.077 |  |
| sprites-single | Sprites 2k | 2000 | engine | SpriteBatch | 0.979 | 0.862 | 2.273 | 0.157 |  |
| sprites-single | Sprites 10k | 10000 | engine | SpriteBatch | 3.704 | 3.306 | 5.561 | 0.762 |  |
| sprites-single | Sprites 50k | 50000 | engine | SpriteBatch | 16.129 | 16.185 | 18.856 | 3.651 |  |
| sprites-single | Tinted sprites 10k | 10000 | engine | SpriteBatch | 3.077 | 2.784 | 4.714 | 0.765 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (in order) | 0.396 | 0.344 | 0.519 | 0.006 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (sorted by texture) | 0.371 | 0.329 | 0.464 | 0.006 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (in order) | 0.691 | 0.661 | 0.916 | 0.538 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (sorted by texture) | 0.695 | 0.605 | 1.479 | 0.095 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (in order) | 5.814 | 5.581 | 7.296 | 5.348 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (sorted by texture) | 1.650 | 1.470 | 3.385 | 0.390 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (in order) | 50.000 | 49.465 | 52.633 | 49.089 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (sorted by texture) | 9.091 | 9.221 | 10.813 | 6.880 |  |
| shapes | Circles 500 | 500 | engine | Apos.Shapes | 0.983 | 0.890 | 2.341 | 0.097 |  |
| shapes | Circles 2k | 2000 | engine | Apos.Shapes | 1.779 | 1.581 | 3.156 | 0.241 |  |
| shapes | Circles 10k | 10000 | engine | Apos.Shapes | 4.608 | 4.273 | 6.081 | 2.711 |  |
| shapes | Mixed shapes 2k | 2000 | engine | Apos.Shapes | 1.842 | 1.653 | 3.207 | 0.359 |  |
| shapes | Mixed shapes 10k | 10000 | engine | Apos.Shapes | 6.849 | 6.439 | 9.213 | 3.334 |  |
| gradients | Gradients 500 | 500 | engine | Apos.Shapes | 1.129 | 0.989 | 2.533 | 0.134 |  |
| gradients | Gradients 2k | 2000 | engine | Apos.Shapes | 1.812 | 1.632 | 3.161 | 0.346 |  |
| gradients | Gradients 10k | 10000 | engine | Apos.Shapes | 5.988 | 5.492 | 7.512 | 4.170 |  |
| shadows | Shadows 100 | 100 | engine | Apos.Shapes | 1.319 | 1.192 | 2.557 | 0.065 |  |
| shadows | Shadows 500 | 500 | engine | Apos.Shapes | 4.630 | 4.182 | 6.027 | 0.934 |  |
| shadows | Shadows 2k | 2000 | engine | Apos.Shapes | 16.949 | 17.073 | 19.002 | 2.992 |  |
| shadows | Shadows 500 unique sizes | 500 | engine | Apos.Shapes | 4.854 | 4.533 | 6.502 | 0.954 |  |
| text | Text 50 | 50 | engine | SpriteFont | 0.596 | 0.519 | 1.049 | 0.067 |  |
| text | Text 50 | 50 | engine | FontStashSharp | 0.612 | 0.532 | 0.761 | 0.066 |  |
| text | Text 200 | 200 | engine | SpriteFont | 0.613 | 0.529 | 0.947 | 0.134 |  |
| text | Text 200 | 200 | engine | FontStashSharp | 0.611 | 0.530 | 0.973 | 0.185 |  |
| text | Text 1k | 1000 | engine | SpriteFont | 0.915 | 0.839 | 1.342 | 0.650 |  |
| text | Text 1k | 1000 | engine | FontStashSharp | 1.166 | 1.111 | 1.460 | 0.939 |  |
| text-mixed | Mixed text 50 | 50 | engine | SpriteFont | 0.629 | 0.546 | 0.853 | 0.133 |  |
| text-mixed | Mixed text 50 | 50 | engine | FontStashSharp | 0.623 | 0.543 | 0.797 | 0.066 |  |
| text-mixed | Mixed text 200 | 200 | engine | SpriteFont | 0.780 | 0.678 | 1.365 | 0.499 |  |
| text-mixed | Mixed text 200 | 200 | engine | FontStashSharp | 0.724 | 0.619 | 1.717 | 0.229 |  |
| text-mixed | Mixed text 1k | 1000 | engine | SpriteFont | 2.398 | 2.337 | 2.619 | 2.164 |  |
| text-mixed | Mixed text 1k | 1000 | engine | FontStashSharp | 1.235 | 1.116 | 2.058 | 0.950 |  |
| svg | SVG 100 | 100 | engine | Apos.Shapes SVG | 1.527 | 1.376 | 2.847 | 0.829 |  |
| svg | SVG 100 | 100 | engine | SpriteBatch (pre-rasterized SVG) | 0.481 | 0.425 | 0.653 | 0.029 |  |
| svg | SVG 500 | 500 | engine | Apos.Shapes SVG | 6.944 | 6.757 | 8.787 | 5.709 |  |
| svg | SVG 500 | 500 | engine | SpriteBatch (pre-rasterized SVG) | 0.963 | 0.853 | 2.385 | 0.082 |  |
| svg | SVG 2k | 2000 | engine | Apos.Shapes SVG | 26.316 | 23.257 | 33.410 | 22.669 |  |
| svg | SVG 2k | 2000 | engine | SpriteBatch (pre-rasterized SVG) | 1.698 | 1.471 | 3.165 | 0.167 |  |
| fill-rate | Opaque 10 layers | 10 | engine | SpriteBatch | 0.757 | 0.679 | 1.332 | 0.028 |  |
| fill-rate | Opaque 10 layers | 10 | engine | Apos.Shapes | 6.579 | 1.932 | 20.545 | 0.112 |  |
| fill-rate | Opaque 40 layers | 40 | engine | SpriteBatch | 1.462 | 1.305 | 2.774 | 0.054 |  |
| fill-rate | Opaque 40 layers | 40 | engine | Apos.Shapes | 23.256 | 4.515 | 87.247 | 0.200 |  |
| fill-rate | Translucent 10 layers | 10 | engine | SpriteBatch | 0.794 | 0.714 | 1.416 | 0.039 |  |
| fill-rate | Translucent 10 layers | 10 | engine | Apos.Shapes | 7.194 | 1.716 | 27.099 | 0.114 |  |
| fill-rate | Translucent 40 layers | 40 | engine | SpriteBatch | 1.513 | 1.367 | 2.768 | 0.053 |  |
| fill-rate | Translucent 40 layers | 40 | engine | Apos.Shapes | 22.222 | 19.818 | 86.524 | 0.203 |  |
| ui | UI 50 | 50 | engine | Apos.Shapes + SpriteBatch | 3.135 | 1.070 | 11.359 | 0.310 |  |
| ui | UI 200 | 200 | engine | Apos.Shapes + SpriteBatch | 4.651 | 1.781 | 17.616 | 0.894 |  |
| ui | UI 1k | 1000 | engine | Apos.Shapes + SpriteBatch | 25.000 | 7.024 | 100.129 | 4.820 |  |
