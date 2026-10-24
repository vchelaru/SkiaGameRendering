# KNI WebGL (Chrome) Raw

- Date: 2026-10-23
- Commit: 1df31c3
- OS: Microsoft Windows 10.0.26300
- CPU: Intel(R) Core(TM) Ultra 9 185H (22 threads)
- GPU: ANGLE (Intel, Intel(R) Arc(TM) Graphics (0x00007D55) Direct3D11 vs_5_0 ps_5_0, D3D11)
- Power: plugged in
- Window: 1600x900, vsync off, 4s measured per entry after 2s warmup

| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Note |
|---|---|---:|---|---|---:|---:|---:|---:|---|
| sprites-single | Sprites 0 | 0 | engine | SpriteBatch | 2759 | 0.300 | 0.800 | 0.000 |  |
| sprites-single | Sprites 500 | 500 | engine | SpriteBatch | 1522 | 0.600 | 1.100 | 0.200 |  |
| sprites-single | Sprites 2k | 2000 | engine | SpriteBatch | 1025 | 0.900 | 1.500 | 0.600 |  |
| sprites-single | Sprites 10k | 10000 | engine | SpriteBatch | 241 | 3.800 | 6.200 | 3.200 |  |
| sprites-single | Sprites 50k | 50000 | engine | SpriteBatch | 66 | 6.800 | 34.900 | 6.400 |  |
| sprites-single | Tinted sprites 10k | 10000 | engine | SpriteBatch | 379 | 1.300 | 21.400 | 1.100 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (in order) | 2724 | 0.200 | 0.400 | 0.000 |  |
| sprites-textures | Textures 0 | 0 | engine | SpriteBatch (sorted by texture) | 2806 | 0.200 | 0.400 | 0.000 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (in order) | 414 | 2.300 | 2.900 | 2.200 |  |
| sprites-textures | Textures 300 | 300 | engine | SpriteBatch (sorted by texture) | 3069 | 0.300 | 0.600 | 0.200 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (in order) | 40 | 21.900 | 61.100 | 21.600 |  |
| sprites-textures | Textures 3k | 3000 | engine | SpriteBatch (sorted by texture) | 534 | 1.700 | 3.000 | 1.400 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (in order) | 2 | 248.000 | 767.000 | 247.300 |  |
| sprites-textures | Textures 30k | 30000 | engine | SpriteBatch (sorted by texture) | 111 | 7.800 | 15.600 | 7.400 |  |
| shapes | Circles 500 | 500 | engine | Apos.Shapes | 405 | 1.800 | 4.700 | 1.200 |  |
| shapes | Circles 2k | 2000 | engine | Apos.Shapes | 108 | 6.400 | 30.800 | 5.200 |  |
| shapes | Circles 10k | 10000 | engine | Apos.Shapes | 22 | 31.500 | 88.800 | 25.400 |  |
| shapes | Mixed shapes 2k | 2000 | engine | Apos.Shapes | 77 | 8.300 | 53.200 | 6.300 |  |
| shapes | Mixed shapes 10k | 10000 | engine | Apos.Shapes | 16 | 74.200 | 103.100 | 72.400 |  |
| gradients | Gradients 500 | 500 | engine | Apos.Shapes | 351 | 1.200 | 3.100 | 0.900 |  |
| gradients | Gradients 2k | 2000 | engine | Apos.Shapes | 95 | 9.000 | 19.900 | 7.900 |  |
| gradients | Gradients 10k | 10000 | engine | Apos.Shapes | 21 | 45.300 | 60.200 | 43.900 |  |
| shadows | Shadows 100 | 100 | engine | Apos.Shapes | 83 | 3.200 | 32.200 | 1.000 |  |
| shadows | Shadows 500 | 500 | engine | Apos.Shapes | 27 | 5.100 | 26.800 | 3.400 |  |
| shadows | Shadows 2k | 2000 | engine | Apos.Shapes | 4 | 12.300 | 1374.900 | 11.200 |  |
| shadows | Shadows 500 unique sizes | 500 | engine | Apos.Shapes | 17 | 1.600 | 4.700 | 1.200 |  |
| text | Text 50 | 50 | engine | SpriteFont | 1882 | 0.300 | 0.900 | 0.200 |  |
| text | Text 50 | 50 | engine | FontStashSharp | 2112 | 0.400 | 0.700 | 0.300 |  |
| text | Text 200 | 200 | engine | SpriteFont | 1520 | 0.600 | 1.100 | 0.400 |  |
| text | Text 200 | 200 | engine | FontStashSharp | 972 | 0.900 | 1.700 | 0.800 |  |
| text | Text 1k | 1000 | engine | SpriteFont | 509 | 1.800 | 2.800 | 1.600 |  |
| text | Text 1k | 1000 | engine | FontStashSharp | 268 | 3.500 | 4.700 | 3.300 |  |
| text-mixed | Mixed text 50 | 50 | engine | SpriteFont | 1258 | 0.700 | 1.200 | 0.600 |  |
| text-mixed | Mixed text 50 | 50 | engine | FontStashSharp | 1874 | 0.500 | 0.900 | 0.300 |  |
| text-mixed | Mixed text 200 | 200 | engine | SpriteFont | 478 | 2.000 | 2.800 | 1.800 |  |
| text-mixed | Mixed text 200 | 200 | engine | FontStashSharp | 886 | 1.000 | 1.800 | 0.900 |  |
| text-mixed | Mixed text 1k | 1000 | engine | SpriteFont | 111 | 8.800 | 10.500 | 8.500 |  |
| text-mixed | Mixed text 1k | 1000 | engine | FontStashSharp | 257 | 3.800 | 4.800 | 3.500 |  |
| svg | SVG 100 | 100 | engine | Apos.Shapes SVG | 214 | 4.200 | 6.700 | 3.800 |  |
| svg | SVG 100 | 100 | engine | SpriteBatch (pre-rasterized SVG) | 2502 | 0.200 | 0.300 | 0.100 |  |
| svg | SVG 500 | 500 | engine | Apos.Shapes SVG | 49 | 19.200 | 25.000 | 18.800 |  |
| svg | SVG 500 | 500 | engine | SpriteBatch (pre-rasterized SVG) | 1848 | 0.200 | 0.400 | 0.100 |  |
| svg | SVG 2k | 2000 | engine | Apos.Shapes SVG | 13 | 76.100 | 112.000 | 75.300 |  |
| svg | SVG 2k | 2000 | engine | SpriteBatch (pre-rasterized SVG) | 994 | 0.500 | 1.300 | 0.300 |  |
| fill-rate | Opaque 10 layers | 10 | engine | SpriteBatch | 1452 | 0.100 | 0.300 | 0.000 |  |
| fill-rate | Opaque 10 layers | 10 | engine | Apos.Shapes | 124 | 5.900 | 22.500 | 5.500 |  |
| fill-rate | Opaque 40 layers | 40 | engine | SpriteBatch | 60 | 16.100 | 28.300 | 0.700 |  |
| fill-rate | Opaque 40 layers | 40 | engine | Apos.Shapes | 43 | 22.900 | 26.700 | 21.500 |  |
| fill-rate | Translucent 10 layers | 10 | engine | SpriteBatch | 131 | 7.500 | 22.000 | 6.700 |  |
| fill-rate | Translucent 10 layers | 10 | engine | Apos.Shapes | 298 | 4.700 | 7.700 | 4.400 |  |
| fill-rate | Translucent 40 layers | 40 | engine | SpriteBatch | 74 | 11.400 | 23.300 | 10.000 |  |
| fill-rate | Translucent 40 layers | 40 | engine | Apos.Shapes | 1 | 23.200 | 36.900 | 21.600 |  |
| ui | UI 50 | 50 | engine | Apos.Shapes + SpriteBatch | 0 | 199.400 | 62389.500 | 198.600 |  |
| ui | UI 200 | 200 | engine | Apos.Shapes + SpriteBatch | 24 | 41.300 | 54.400 | 39.900 |  |
| ui | UI 1k | 1000 | engine | Apos.Shapes + SpriteBatch | 5 | 195.500 | 217.400 | 194.600 |  |
