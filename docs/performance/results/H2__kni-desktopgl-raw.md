# KNI DesktopGL Raw

- Date: 2026-10-23
- Commit: 1df31c3
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | engine | SpriteBatch | 2132 | 0.454 | 0.552 | 0.007 |  |
| sprites-single | Sprites 500 | 500 | engine | SpriteBatch | 1945 | 0.493 | 0.724 | 0.058 |  |
| sprites-single | Sprites 2k | 2000 | engine | SpriteBatch | 1335 | 0.732 | 0.912 | 0.127 |  |
| sprites-single | Sprites 10k | 10000 | engine | SpriteBatch | 364 | 2.684 | 3.211 | 0.562 |  |
| sprites-single | Sprites 50k | 50000 | engine | SpriteBatch | 76 | 13.239 | 13.808 | 3.269 |  |
| sprites-single | Tinted sprites 10k | 10000 | engine | SpriteBatch | 399 | 2.443 | 2.873 | 0.617 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (in order) | 3053 | 0.318 | 0.387 | 0.006 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (sorted by texture) | 3062 | 0.317 | 0.379 | 0.007 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (in order) | 1573 | 0.593 | 0.828 | 0.479 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (sorted by texture) | 2111 | 0.462 | 0.538 | 0.072 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (in order) | 199 | 4.758 | 6.895 | 4.616 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (sorted by texture) | 949 | 1.035 | 1.333 | 0.354 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (in order) | 21 | 47.378 | 50.753 | 47.119 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (sorted by texture) | 124 | 7.979 | 8.633 | 3.612 |  |
| shapes | Circles 500 | 500 | engine | Apos.Shapes | 1637 | 0.599 | 0.691 | 0.079 |  |
| shapes | Circles 2k | 2000 | engine | Apos.Shapes | 852 | 1.153 | 1.467 | 0.228 |  |
| shapes | Circles 10k | 10000 | engine | Apos.Shapes | 270 | 3.594 | 4.235 | 0.955 |  |
| shapes | Mixed shapes 2k | 2000 | engine | Apos.Shapes | 679 | 1.440 | 1.747 | 0.263 |  |
| shapes | Mixed shapes 10k | 10000 | engine | Apos.Shapes | 170 | 5.655 | 7.040 | 3.080 |  |
| gradients | Gradients 500 | 500 | engine | Apos.Shapes | 1451 | 0.665 | 1.014 | 0.113 |  |
| gradients | Gradients 2k | 2000 | engine | Apos.Shapes | 711 | 1.388 | 1.697 | 0.358 |  |
| gradients | Gradients 10k | 10000 | engine | Apos.Shapes | 191 | 5.021 | 6.643 | 3.998 |  |
| shadows | Shadows 100 | 100 | engine | Apos.Shapes | 890 | 1.104 | 1.372 | 0.109 |  |
| shadows | Shadows 500 | 500 | engine | Apos.Shapes | 239 | 4.103 | 4.650 | 0.481 |  |
| shadows | Shadows 2k | 2000 | engine | Apos.Shapes | 64 | 15.720 | 16.354 | 3.012 |  |
| shadows | Shadows 500 unique sizes | 500 | engine | Apos.Shapes | 243 | 4.057 | 4.561 | 0.548 |  |
| text | Text 50 | 50 | engine | SpriteFont | 2627 | 0.370 | 0.453 | 0.052 |  |
| text | Text 50 | 50 | engine | FontStashSharp | 2641 | 0.368 | 0.444 | 0.067 |  |
| text | Text 200 | 200 | engine | SpriteFont | 2295 | 0.423 | 0.527 | 0.115 |  |
| text | Text 200 | 200 | engine | FontStashSharp | 2295 | 0.423 | 0.593 | 0.193 |  |
| text | Text 1k | 1000 | engine | SpriteFont | 1383 | 0.695 | 0.953 | 0.529 |  |
| text | Text 1k | 1000 | engine | FontStashSharp | 997 | 0.954 | 1.236 | 0.821 |  |
| text-mixed | Mixed text 50 | 50 | engine | SpriteFont | 2512 | 0.388 | 0.505 | 0.169 |  |
| text-mixed | Mixed text 50 | 50 | engine | FontStashSharp | 2535 | 0.384 | 0.444 | 0.068 |  |
| text-mixed | Mixed text 200 | 200 | engine | SpriteFont | 1622 | 0.595 | 0.751 | 0.449 |  |
| text-mixed | Mixed text 200 | 200 | engine | FontStashSharp | 2037 | 0.477 | 0.648 | 0.181 |  |
| text-mixed | Mixed text 1k | 1000 | engine | SpriteFont | 426 | 2.276 | 2.616 | 2.134 |  |
| text-mixed | Mixed text 1k | 1000 | engine | FontStashSharp | 945 | 1.011 | 1.230 | 0.875 |  |
| svg | SVG 100 | 100 | engine | Apos.Shapes SVG | 655 | 1.505 | 1.959 | 0.742 |  |
| svg | SVG 100 | 100 | engine | SpriteBatch (pre-rasterized SVG) | 2381 | 0.408 | 0.536 | 0.034 |  |
| svg | SVG 500 | 500 | engine | Apos.Shapes SVG | 161 | 6.199 | 8.953 | 5.160 |  |
| svg | SVG 500 | 500 | engine | SpriteBatch (pre-rasterized SVG) | 1735 | 0.561 | 0.674 | 0.054 |  |
| svg | SVG 2k | 2000 | engine | Apos.Shapes SVG | 32 | 19.195 | 104.068 | 18.580 |  |
| svg | SVG 2k | 2000 | engine | SpriteBatch (pre-rasterized SVG) | 839 | 1.155 | 1.512 | 0.159 |  |
| fill-rate | Opaque 10 layers | 10 | engine | SpriteBatch | 1570 | 0.619 | 0.747 | 0.032 |  |
| fill-rate | Opaque 10 layers | 10 | engine | Apos.Shapes | 194 | 0.594 | 18.371 | 0.097 |  |
| fill-rate | Opaque 40 layers | 40 | engine | SpriteBatch | 780 | 1.221 | 1.603 | 0.066 |  |
| fill-rate | Opaque 40 layers | 40 | engine | Apos.Shapes | 45 | 16.284 | 82.088 | 0.155 |  |
| fill-rate | Translucent 10 layers | 10 | engine | SpriteBatch | 1515 | 0.640 | 0.773 | 0.036 |  |
| fill-rate | Translucent 10 layers | 10 | engine | Apos.Shapes | 158 | 0.688 | 18.784 | 0.103 |  |
| fill-rate | Translucent 40 layers | 40 | engine | SpriteBatch | 761 | 1.276 | 1.661 | 0.093 |  |
| fill-rate | Translucent 40 layers | 40 | engine | Apos.Shapes | 49 | 0.745 | 80.970 | 0.147 |  |
| ui | UI 50 | 50 | engine | Apos.Shapes + SpriteBatch | 649 | 0.598 | 4.879 | 0.261 |  |
| ui | UI 200 | 200 | engine | Apos.Shapes + SpriteBatch | 115 | 2.473 | 31.565 | 1.160 |  |
| ui | UI 1k | 1000 | engine | Apos.Shapes + SpriteBatch | 63 | 5.826 | 53.212 | 4.825 |  |
