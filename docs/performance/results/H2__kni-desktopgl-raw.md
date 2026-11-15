# KNI DesktopGL Raw

- Date: 2026-10-23
- Commit: 1df31c3
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | Mean frame ms | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | engine | SpriteBatch | 0.469 | 0.454 | 0.552 | 0.007 |  |
| sprites-single | Sprites 500 | 500 | engine | SpriteBatch | 0.514 | 0.493 | 0.724 | 0.058 |  |
| sprites-single | Sprites 2k | 2000 | engine | SpriteBatch | 0.749 | 0.732 | 0.912 | 0.127 |  |
| sprites-single | Sprites 10k | 10000 | engine | SpriteBatch | 2.747 | 2.684 | 3.211 | 0.562 |  |
| sprites-single | Sprites 50k | 50000 | engine | SpriteBatch | 13.158 | 13.239 | 13.808 | 3.269 |  |
| sprites-single | Tinted sprites 10k | 10000 | engine | SpriteBatch | 2.506 | 2.443 | 2.873 | 0.617 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (in order) | 0.328 | 0.318 | 0.387 | 0.006 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (sorted by texture) | 0.327 | 0.317 | 0.379 | 0.007 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (in order) | 0.636 | 0.593 | 0.828 | 0.479 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (sorted by texture) | 0.474 | 0.462 | 0.538 | 0.072 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (in order) | 5.025 | 4.758 | 6.895 | 4.616 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (sorted by texture) | 1.054 | 1.035 | 1.333 | 0.354 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (in order) | 47.619 | 47.378 | 50.753 | 47.119 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (sorted by texture) | 8.065 | 7.979 | 8.633 | 3.612 |  |
| shapes | Circles 500 | 500 | engine | Apos.Shapes | 0.611 | 0.599 | 0.691 | 0.079 |  |
| shapes | Circles 2k | 2000 | engine | Apos.Shapes | 1.174 | 1.153 | 1.467 | 0.228 |  |
| shapes | Circles 10k | 10000 | engine | Apos.Shapes | 3.704 | 3.594 | 4.235 | 0.955 |  |
| shapes | Mixed shapes 2k | 2000 | engine | Apos.Shapes | 1.473 | 1.440 | 1.747 | 0.263 |  |
| shapes | Mixed shapes 10k | 10000 | engine | Apos.Shapes | 5.882 | 5.655 | 7.040 | 3.080 |  |
| gradients | Gradients 500 | 500 | engine | Apos.Shapes | 0.689 | 0.665 | 1.014 | 0.113 |  |
| gradients | Gradients 2k | 2000 | engine | Apos.Shapes | 1.406 | 1.388 | 1.697 | 0.358 |  |
| gradients | Gradients 10k | 10000 | engine | Apos.Shapes | 5.236 | 5.021 | 6.643 | 3.998 |  |
| shadows | Shadows 100 | 100 | engine | Apos.Shapes | 1.124 | 1.104 | 1.372 | 0.109 |  |
| shadows | Shadows 500 | 500 | engine | Apos.Shapes | 4.184 | 4.103 | 4.650 | 0.481 |  |
| shadows | Shadows 2k | 2000 | engine | Apos.Shapes | 15.625 | 15.720 | 16.354 | 3.012 |  |
| shadows | Shadows 500 unique sizes | 500 | engine | Apos.Shapes | 4.115 | 4.057 | 4.561 | 0.548 |  |
| text | Text 50 | 50 | engine | SpriteFont | 0.381 | 0.370 | 0.453 | 0.052 |  |
| text | Text 50 | 50 | engine | FontStashSharp | 0.379 | 0.368 | 0.444 | 0.067 |  |
| text | Text 200 | 200 | engine | SpriteFont | 0.436 | 0.423 | 0.527 | 0.115 |  |
| text | Text 200 | 200 | engine | FontStashSharp | 0.436 | 0.423 | 0.593 | 0.193 |  |
| text | Text 1k | 1000 | engine | SpriteFont | 0.723 | 0.695 | 0.953 | 0.529 |  |
| text | Text 1k | 1000 | engine | FontStashSharp | 1.003 | 0.954 | 1.236 | 0.821 |  |
| text-mixed | Mixed text 50 | 50 | engine | SpriteFont | 0.398 | 0.388 | 0.505 | 0.169 |  |
| text-mixed | Mixed text 50 | 50 | engine | FontStashSharp | 0.394 | 0.384 | 0.444 | 0.068 |  |
| text-mixed | Mixed text 200 | 200 | engine | SpriteFont | 0.617 | 0.595 | 0.751 | 0.449 |  |
| text-mixed | Mixed text 200 | 200 | engine | FontStashSharp | 0.491 | 0.477 | 0.648 | 0.181 |  |
| text-mixed | Mixed text 1k | 1000 | engine | SpriteFont | 2.347 | 2.276 | 2.616 | 2.134 |  |
| text-mixed | Mixed text 1k | 1000 | engine | FontStashSharp | 1.058 | 1.011 | 1.230 | 0.875 |  |
| svg | SVG 100 | 100 | engine | Apos.Shapes SVG | 1.527 | 1.505 | 1.959 | 0.742 |  |
| svg | SVG 100 | 100 | engine | SpriteBatch (pre-rasterized SVG) | 0.420 | 0.408 | 0.536 | 0.034 |  |
| svg | SVG 500 | 500 | engine | Apos.Shapes SVG | 6.211 | 6.199 | 8.953 | 5.160 |  |
| svg | SVG 500 | 500 | engine | SpriteBatch (pre-rasterized SVG) | 0.576 | 0.561 | 0.674 | 0.054 |  |
| svg | SVG 2k | 2000 | engine | Apos.Shapes SVG | 31.250 | 19.195 | 104.068 | 18.580 |  |
| svg | SVG 2k | 2000 | engine | SpriteBatch (pre-rasterized SVG) | 1.192 | 1.155 | 1.512 | 0.159 |  |
| fill-rate | Opaque 10 layers | 10 | engine | SpriteBatch | 0.637 | 0.619 | 0.747 | 0.032 |  |
| fill-rate | Opaque 10 layers | 10 | engine | Apos.Shapes | 5.155 | 0.594 | 18.371 | 0.097 |  |
| fill-rate | Opaque 40 layers | 40 | engine | SpriteBatch | 1.282 | 1.221 | 1.603 | 0.066 |  |
| fill-rate | Opaque 40 layers | 40 | engine | Apos.Shapes | 22.222 | 16.284 | 82.088 | 0.155 |  |
| fill-rate | Translucent 10 layers | 10 | engine | SpriteBatch | 0.660 | 0.640 | 0.773 | 0.036 |  |
| fill-rate | Translucent 10 layers | 10 | engine | Apos.Shapes | 6.329 | 0.688 | 18.784 | 0.103 |  |
| fill-rate | Translucent 40 layers | 40 | engine | SpriteBatch | 1.314 | 1.276 | 1.661 | 0.093 |  |
| fill-rate | Translucent 40 layers | 40 | engine | Apos.Shapes | 20.408 | 0.745 | 80.970 | 0.147 |  |
| ui | UI 50 | 50 | engine | Apos.Shapes + SpriteBatch | 1.541 | 0.598 | 4.879 | 0.261 |  |
| ui | UI 200 | 200 | engine | Apos.Shapes + SpriteBatch | 8.696 | 2.473 | 31.565 | 1.160 |  |
| ui | UI 1k | 1000 | engine | Apos.Shapes + SpriteBatch | 15.873 | 5.826 | 53.212 | 4.825 |  |
