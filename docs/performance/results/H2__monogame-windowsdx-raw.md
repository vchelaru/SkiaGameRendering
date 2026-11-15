# MonoGame WindowsDX Raw

- Date: 2026-10-23
- Commit: 2a3a7c6
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: Intel(R) Arc(TM) Graphics
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | Mean frame ms | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | engine | SpriteBatch | 0.374 | 0.358 | 0.442 | 0.001 |  |
| sprites-single | Sprites 500 | 500 | engine | SpriteBatch | 0.507 | 0.497 | 0.615 | 0.030 |  |
| sprites-single | Sprites 2k | 2000 | engine | SpriteBatch | 0.781 | 0.763 | 0.958 | 0.100 |  |
| sprites-single | Sprites 10k | 10000 | engine | SpriteBatch | 2.762 | 2.682 | 3.250 | 0.629 |  |
| sprites-single | Sprites 50k | 50000 | engine | SpriteBatch | 13.514 | 13.574 | 14.431 | 2.864 |  |
| sprites-single | Tinted sprites 10k | 10000 | engine | SpriteBatch | 2.551 | 2.484 | 2.987 | 0.496 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (in order) | 0.388 | 0.380 | 0.447 | 0.001 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (sorted by texture) | 0.374 | 0.358 | 0.440 | 0.001 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (in order) | 0.461 | 0.446 | 0.519 | 0.160 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (sorted by texture) | 0.484 | 0.467 | 0.590 | 0.034 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (in order) | 1.582 | 1.520 | 2.044 | 1.361 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (sorted by texture) | 1.053 | 1.030 | 1.315 | 0.309 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (in order) | 14.286 | 14.005 | 16.757 | 13.473 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (sorted by texture) | 8.197 | 8.165 | 8.868 | 3.685 |  |
| shapes | Circles 500 | 500 | engine | Apos.Shapes | 0.638 | 0.616 | 0.752 | 0.070 |  |
| shapes | Circles 2k | 2000 | engine | Apos.Shapes | 1.279 | 1.244 | 1.601 | 0.262 |  |
| shapes | Circles 10k | 10000 | engine | Apos.Shapes | 4.255 | 4.228 | 4.991 | 1.395 |  |
| shapes | Mixed shapes 2k | 2000 | engine | Apos.Shapes | 1.675 | 1.625 | 2.049 | 0.610 |  |
| shapes | Mixed shapes 10k | 10000 | engine | Apos.Shapes | 7.092 | 6.894 | 8.988 | 3.087 |  |
| gradients | Gradients 500 | 500 | engine | Apos.Shapes | 0.735 | 0.712 | 0.939 | 0.095 |  |
| gradients | Gradients 2k | 2000 | engine | Apos.Shapes | 1.631 | 1.569 | 1.987 | 0.389 |  |
| gradients | Gradients 10k | 10000 | engine | Apos.Shapes | 5.988 | 5.850 | 6.535 | 3.645 |  |
| shadows | Shadows 100 | 100 | engine | Apos.Shapes | 1.266 | 1.228 | 1.536 | 0.036 |  |
| shadows | Shadows 500 | 500 | engine | Apos.Shapes | 5.155 | 5.041 | 5.761 | 0.457 |  |
| shadows | Shadows 2k | 2000 | engine | Apos.Shapes | 18.868 | 19.206 | 19.852 | 1.497 |  |
| shadows | Shadows 500 unique sizes | 500 | engine | Apos.Shapes | 4.878 | 4.879 | 5.701 | 0.270 |  |
| text | Text 50 | 50 | engine | SpriteFont | 0.398 | 0.385 | 0.452 | 0.036 |  |
| text | Text 50 | 50 | engine | FontStashSharp | 0.394 | 0.382 | 0.443 | 0.051 |  |
| text | Text 200 | 200 | engine | SpriteFont | 0.447 | 0.433 | 0.492 | 0.117 |  |
| text | Text 200 | 200 | engine | FontStashSharp | 0.442 | 0.428 | 0.522 | 0.166 |  |
| text | Text 1k | 1000 | engine | SpriteFont | 0.847 | 0.818 | 1.093 | 0.681 |  |
| text | Text 1k | 1000 | engine | FontStashSharp | 1.042 | 0.998 | 1.322 | 0.852 |  |
| text-mixed | Mixed text 50 | 50 | engine | SpriteFont | 0.424 | 0.408 | 0.516 | 0.049 |  |
| text-mixed | Mixed text 50 | 50 | engine | FontStashSharp | 0.407 | 0.394 | 0.477 | 0.082 |  |
| text-mixed | Mixed text 200 | 200 | engine | SpriteFont | 0.516 | 0.500 | 0.588 | 0.232 |  |
| text-mixed | Mixed text 200 | 200 | engine | FontStashSharp | 0.492 | 0.476 | 0.587 | 0.172 |  |
| text-mixed | Mixed text 1k | 1000 | engine | SpriteFont | 1.280 | 1.237 | 1.535 | 1.080 |  |
| text-mixed | Mixed text 1k | 1000 | engine | FontStashSharp | 1.111 | 1.048 | 1.485 | 0.899 |  |
| svg | SVG 100 | 100 | engine | Apos.Shapes SVG | 1.656 | 1.613 | 2.036 | 0.644 |  |
| svg | SVG 100 | 100 | engine | SpriteBatch (pre-rasterized SVG) | 0.432 | 0.417 | 0.556 | 0.010 |  |
| svg | SVG 500 | 500 | engine | Apos.Shapes SVG | 6.623 | 6.631 | 7.442 | 4.308 |  |
| svg | SVG 500 | 500 | engine | SpriteBatch (pre-rasterized SVG) | 0.590 | 0.572 | 0.712 | 0.028 |  |
| svg | SVG 2k | 2000 | engine | Apos.Shapes SVG | 25.641 | 25.750 | 30.932 | 20.946 |  |
| svg | SVG 2k | 2000 | engine | SpriteBatch (pre-rasterized SVG) | 1.188 | 1.159 | 1.501 | 0.105 |  |
| fill-rate | Opaque 10 layers | 10 | engine | SpriteBatch | 0.648 | 0.628 | 0.758 | 0.007 |  |
| fill-rate | Opaque 10 layers | 10 | engine | Apos.Shapes | 5.556 | 5.414 | 6.169 | 0.027 |  |
| fill-rate | Opaque 40 layers | 40 | engine | SpriteBatch | 1.290 | 1.257 | 1.616 | 0.016 |  |
| fill-rate | Opaque 40 layers | 40 | engine | Apos.Shapes | 22.222 | 21.995 | 22.604 | 0.085 |  |
| fill-rate | Translucent 10 layers | 10 | engine | SpriteBatch | 0.676 | 0.657 | 0.821 | 0.008 |  |
| fill-rate | Translucent 10 layers | 10 | engine | Apos.Shapes | 5.814 | 5.600 | 6.216 | 0.073 |  |
| fill-rate | Translucent 40 layers | 40 | engine | SpriteBatch | 1.387 | 1.291 | 1.732 | 0.016 |  |
| fill-rate | Translucent 40 layers | 40 | engine | Apos.Shapes | 22.727 | 22.573 | 22.820 | 0.110 |  |
| ui | UI 50 | 50 | engine | Apos.Shapes + SpriteBatch | 1.299 | 1.256 | 1.621 | 0.068 |  |
| ui | UI 200 | 200 | engine | Apos.Shapes + SpriteBatch | 4.115 | 4.021 | 4.581 | 0.391 |  |
| ui | UI 1k | 1000 | engine | Apos.Shapes + SpriteBatch | 18.868 | 18.653 | 19.231 | 7.342 |  |
