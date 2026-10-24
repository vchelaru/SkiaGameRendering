# MonoGame WindowsDX Raw

- Date: 2026-10-23
- Commit: 2a3a7c6
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | engine | SpriteBatch | 2677 | 0.358 | 0.442 | 0.001 |  |
| sprites-single | Sprites 500 | 500 | engine | SpriteBatch | 1973 | 0.497 | 0.615 | 0.030 |  |
| sprites-single | Sprites 2k | 2000 | engine | SpriteBatch | 1281 | 0.763 | 0.958 | 0.100 |  |
| sprites-single | Sprites 10k | 10000 | engine | SpriteBatch | 362 | 2.682 | 3.250 | 0.629 |  |
| sprites-single | Sprites 50k | 50000 | engine | SpriteBatch | 74 | 13.574 | 14.431 | 2.864 |  |
| sprites-single | Tinted sprites 10k | 10000 | engine | SpriteBatch | 392 | 2.484 | 2.987 | 0.496 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (in order) | 2575 | 0.380 | 0.447 | 0.001 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (sorted by texture) | 2674 | 0.358 | 0.440 | 0.001 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (in order) | 2170 | 0.446 | 0.519 | 0.160 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (sorted by texture) | 2065 | 0.467 | 0.590 | 0.034 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (in order) | 632 | 1.520 | 2.044 | 1.361 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (sorted by texture) | 950 | 1.030 | 1.315 | 0.309 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (in order) | 70 | 14.005 | 16.757 | 13.473 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (sorted by texture) | 122 | 8.165 | 8.868 | 3.685 |  |
| shapes | Circles 500 | 500 | engine | Apos.Shapes | 1567 | 0.616 | 0.752 | 0.070 |  |
| shapes | Circles 2k | 2000 | engine | Apos.Shapes | 782 | 1.244 | 1.601 | 0.262 |  |
| shapes | Circles 10k | 10000 | engine | Apos.Shapes | 235 | 4.228 | 4.991 | 1.395 |  |
| shapes | Mixed shapes 2k | 2000 | engine | Apos.Shapes | 597 | 1.625 | 2.049 | 0.610 |  |
| shapes | Mixed shapes 10k | 10000 | engine | Apos.Shapes | 141 | 6.894 | 8.988 | 3.087 |  |
| gradients | Gradients 500 | 500 | engine | Apos.Shapes | 1361 | 0.712 | 0.939 | 0.095 |  |
| gradients | Gradients 2k | 2000 | engine | Apos.Shapes | 613 | 1.569 | 1.987 | 0.389 |  |
| gradients | Gradients 10k | 10000 | engine | Apos.Shapes | 167 | 5.850 | 6.535 | 3.645 |  |
| shadows | Shadows 100 | 100 | engine | Apos.Shapes | 790 | 1.228 | 1.536 | 0.036 |  |
| shadows | Shadows 500 | 500 | engine | Apos.Shapes | 194 | 5.041 | 5.761 | 0.457 |  |
| shadows | Shadows 2k | 2000 | engine | Apos.Shapes | 53 | 19.206 | 19.852 | 1.497 |  |
| shadows | Shadows 500 unique sizes | 500 | engine | Apos.Shapes | 205 | 4.879 | 5.701 | 0.270 |  |
| text | Text 50 | 50 | engine | SpriteFont | 2511 | 0.385 | 0.452 | 0.036 |  |
| text | Text 50 | 50 | engine | FontStashSharp | 2535 | 0.382 | 0.443 | 0.051 |  |
| text | Text 200 | 200 | engine | SpriteFont | 2239 | 0.433 | 0.492 | 0.117 |  |
| text | Text 200 | 200 | engine | FontStashSharp | 2261 | 0.428 | 0.522 | 0.166 |  |
| text | Text 1k | 1000 | engine | SpriteFont | 1180 | 0.818 | 1.093 | 0.681 |  |
| text | Text 1k | 1000 | engine | FontStashSharp | 960 | 0.998 | 1.322 | 0.852 |  |
| text-mixed | Mixed text 50 | 50 | engine | SpriteFont | 2356 | 0.408 | 0.516 | 0.049 |  |
| text-mixed | Mixed text 50 | 50 | engine | FontStashSharp | 2455 | 0.394 | 0.477 | 0.082 |  |
| text-mixed | Mixed text 200 | 200 | engine | SpriteFont | 1938 | 0.500 | 0.588 | 0.232 |  |
| text-mixed | Mixed text 200 | 200 | engine | FontStashSharp | 2032 | 0.476 | 0.587 | 0.172 |  |
| text-mixed | Mixed text 1k | 1000 | engine | SpriteFont | 781 | 1.237 | 1.535 | 1.080 |  |
| text-mixed | Mixed text 1k | 1000 | engine | FontStashSharp | 900 | 1.048 | 1.485 | 0.899 |  |
| svg | SVG 100 | 100 | engine | Apos.Shapes SVG | 604 | 1.613 | 2.036 | 0.644 |  |
| svg | SVG 100 | 100 | engine | SpriteBatch (pre-rasterized SVG) | 2317 | 0.417 | 0.556 | 0.010 |  |
| svg | SVG 500 | 500 | engine | Apos.Shapes SVG | 151 | 6.631 | 7.442 | 4.308 |  |
| svg | SVG 500 | 500 | engine | SpriteBatch (pre-rasterized SVG) | 1694 | 0.572 | 0.712 | 0.028 |  |
| svg | SVG 2k | 2000 | engine | Apos.Shapes SVG | 39 | 25.750 | 30.932 | 20.946 |  |
| svg | SVG 2k | 2000 | engine | SpriteBatch (pre-rasterized SVG) | 842 | 1.159 | 1.501 | 0.105 |  |
| fill-rate | Opaque 10 layers | 10 | engine | SpriteBatch | 1543 | 0.628 | 0.758 | 0.007 |  |
| fill-rate | Opaque 10 layers | 10 | engine | Apos.Shapes | 180 | 5.414 | 6.169 | 0.027 |  |
| fill-rate | Opaque 40 layers | 40 | engine | SpriteBatch | 775 | 1.257 | 1.616 | 0.016 |  |
| fill-rate | Opaque 40 layers | 40 | engine | Apos.Shapes | 45 | 21.995 | 22.604 | 0.085 |  |
| fill-rate | Translucent 10 layers | 10 | engine | SpriteBatch | 1479 | 0.657 | 0.821 | 0.008 |  |
| fill-rate | Translucent 10 layers | 10 | engine | Apos.Shapes | 172 | 5.600 | 6.216 | 0.073 |  |
| fill-rate | Translucent 40 layers | 40 | engine | SpriteBatch | 721 | 1.291 | 1.732 | 0.016 |  |
| fill-rate | Translucent 40 layers | 40 | engine | Apos.Shapes | 44 | 22.573 | 22.820 | 0.110 |  |
| ui | UI 50 | 50 | engine | Apos.Shapes + SpriteBatch | 770 | 1.256 | 1.621 | 0.068 |  |
| ui | UI 200 | 200 | engine | Apos.Shapes + SpriteBatch | 243 | 4.021 | 4.581 | 0.391 |  |
| ui | UI 1k | 1000 | engine | Apos.Shapes + SpriteBatch | 53 | 18.653 | 19.231 | 7.342 |  |
