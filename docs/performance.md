# Performance

How SkiaGameRendering compares with the engine's own drawing, scene by scene, platform by platform. Each
scene has a page with a screenshot, what each side does, and a table per platform and machine.

| Page | What it draws |
|---|---|
| [Sprites, one texture](performance/sprites-single.md) | One image, thousands of times, with rotation and tint |
| [Sprites, ten textures](performance/sprites-textures.md) | Ten images cycled 1..10 in a fixed order |
| [Shapes](performance/shapes.md) | Circles, rounded rectangles, lines, triangles |
| [Gradients](performance/gradients.md) | Rotating gradient-filled rounded rectangles |
| [Drop shadows](performance/shadows.md) | Panels with a blurred shadow |
| [Text, one font](performance/text.md) | Labels in one size |
| [Text, mixed fonts](performance/text-mixed.md) | Labels in six sizes |
| [SVG](performance/svg.md) | A vector icon at variable sizes |
| [Fill rate](performance/fill-rate.md) | Screen-sized layers, opaque and translucent |
| [UI panels](performance/ui.md) | Cards with shadow, border, icon and text |

## Tips

- **If sprites must go through Skia, use `SKCanvas.DrawAtlas`.** From about 10k sprites one `DrawAtlas`
  call per texture sheet reaches 65-100% of `SpriteBatch`'s frame rate, depending on the backend; below
  that the fixed cost of a Skia pass (about 0.1-0.3 ms) dominates. One `DrawImage` per sprite is about
  three times slower than `DrawAtlas`.
- **Tint through `DrawAtlas`'s `colors` array with `SKBlendMode.Modulate`**, the equivalent of
  `SpriteBatch.Draw`'s `color`. It costs almost nothing extra. Tinting `DrawImage` with a per-sprite
  `SKPaint.ColorFilter` is the slowest option measured, about six times slower than the tinted
  `DrawAtlas`. `SKColor` is unpremultiplied, so the matching `SpriteBatch` color is
  `Color.FromNonPremultiplied(r, g, b, a)`.
- **Upload sprite images once** with `SKImage.ToTextureImage(SkiaRenderer.CurrentBackend.GRContext)`,
  called between `Begin()` and `End()` when Skia's GPU context is current, and reuse the result.
- **Use few, large targets.** Every `Begin()`/`End()` pair switches GPU context and flushes Skia. On
  WindowsDX and KNI WindowsDX, `End()` also calls `glFinish`, which stalls the CPU until the GPU
  finishes; `SkiaDirectCanvas.End()` does not. One screen-sized target per frame beats one per element.
- **Prefer `SkiaDirectCanvas` where it is supported.** It skips the full-screen clear and composite of a
  `SkiaRenderTarget2D`: an empty pass costs about 0.07-0.1 ms instead of 0.15-0.3 ms, and a scene that
  fills the screen gains the most.
- **Do not replay an SVG every frame for many icons.** It is the slowest thing in the suite on desktop
  OpenGL (about 4 FPS for 2k icons, against 32 for Apos.Shapes). Draw a bitmap rendered once instead.
- **Blurred shadows through `SKMaskFilter.CreateBlur` are eight to eleven times slower** than a shadow
  computed in a shader (Apos.Shapes), and gradients and rounded-rectangle strokes are several times slower
  too. For many shapes of those kinds on the desktop, a shader-based library is the faster choice.

## How it is measured

Each platform has two apps under `benchmarks/Performance/`, built from the same scene code
(`Shared/`, linked into every app):

- **Raw** draws with the engine alone: `SpriteBatch`, [Apos.Shapes](https://github.com/Apostolique/Apos.Shapes)
  for shapes, gradients, shadows and SVG, a `SpriteFont` baked from the TTF (one per size) and
  FontStashSharp for text. Skia is not loaded.
- **Skia** draws the same scenes with `SKCanvas` calls only, sprites through `DrawAtlas`. Every scene runs
  twice, through a `SkiaRenderTarget2D` (the "render target" column: Skia draws into a texture, then it is
  composited) and through a `SkiaDirectCanvas` (the "direct" column: Skia draws straight into the back buffer).

Every scene is deterministic: positions, sizes, textures and colors come from the item index, so every run
and every platform draws the same thing. Both apps run at 1600x900 with vsync and the fixed timestep off.
Each scene gets a 2 s warmup and a 4 s measured window. **FPS** is the measured frames over the window's
wall-clock time, so it covers the whole frame: update, draw submission, the Skia context switch and flush,
the composite and `Present`. The tables also give the median draw CPU time (the time inside the draw code
alone) and the median and 95th-percentile frame time. A cell marked `*` had a frame over 20 times the median
(a shader compile or a garbage collection), which pulls its FPS down.

The browser apps run from `requestAnimationFrame`, started with the browser's frame-rate limit and vsync off
so it does not cap at the display's refresh rate, as an ahead-of-time compiled (AOT) WebAssembly publish.
Compare browser rows with desktop rows with care: the managed code (`SpriteBatch`, `Apos.Shapes`) runs
as WebAssembly while Skia is native code compiled to WebAssembly.

## Running

Desktop (any scene subset with `--page slug[,slug]`; `--quick` shortens the windows for a smoke run;
`--no-screenshots` skips the PNGs):

```bash
dotnet build benchmarks/Performance/Kni.DesktopGL/Performance.Kni.DesktopGL.sln -c Release
benchmarks/Performance/Kni.DesktopGL/Raw/bin/Release/net8.0/Performance.Kni.DesktopGL.Raw
benchmarks/Performance/Kni.DesktopGL/Skia/bin/Release/net8.0/Performance.Kni.DesktopGL.Skia
```

`Performance.MonoGame.DesktopGL.sln` and `Performance.MonoGame.WindowsDX.sln` work the same way. Each app
writes `perf-results-<app>.md` and a `screenshots` folder next to the exe, and exits when done.

Browser (needs the `wasm-tools-net8` workload and Node):

```bash
dotnet publish benchmarks/Performance/Kni.WebGL/Raw/Performance.Kni.WebGL.Raw.csproj -c Release -p:RunAOTCompilation=true
npm install --prefix benchmarks/Performance/Kni.WebGL
node benchmarks/Performance/Kni.WebGL/serve.mjs benchmarks/Performance/Kni.WebGL/Raw/bin/Release/net8.0/publish/wwwroot 5101
node benchmarks/Performance/Kni.WebGL/run-browser.mjs --url http://127.0.0.1:5101/ --browser chromium --channel chrome --out results
```

`run-browser.mjs` also takes `--browser firefox` (with `--firefox-path` for a stock install) and the
`--os`, `--cpu`, `--power` and `--commit` a browser cannot see, so the results header matches the desktop
runs on the same machine. Do the same for `Skia`.

Laptops need to be plugged in and on the GPU you mean to measure; the results file records which GPU ran.
Run one app at a time: two at once distort each other. A run longer than your shell allows can be split with
`--page` and the files dropped in as `...-1.md`, `...-2.md`.

## Adding results

Copy the `perf-results-*.md` files into `docs/performance/results/`, renamed
`<hardware-id>__<platform>-<app>.md` (see its README), then regenerate the tables:

```bash
pwsh eng/update-performance-docs.ps1
```

The script rewrites the hardware table below and the table between the `results` markers on each page. The
text around them is written by hand.

## Hardware

<!-- hardware:begin -->

| ID | CPU | GPU | OS | Power | Platforms run |
|---|---|---|---|---|---|
| H1 | AMD Ryzen 7 260 (16 threads) | NVIDIA GeForce RTX 5060 Laptop GPU | Windows 11 (26200) | Plugged in | MonoGame DesktopGL, MonoGame WindowsDX |
| H2 | Intel(R) Core(TM) Ultra 9 185H (22 threads) | Intel(R) Arc(TM) Graphics; ANGLE (Intel, Intel(R) Arc(TM) Graphics (0x00007D55) Direct3D11 vs_5_0 ps_5_0, D3D11) | Microsoft Windows 10.0.26300 | plugged in | MonoGame DesktopGL, MonoGame WindowsDX, KNI DesktopGL, KNI WebGL (Chrome) |

<!-- hardware:end -->

## What is not measured yet

- **MonoGame WindowsDX, Skia side.** `Sample.MonoGame.WindowsDX` and the Skia performance app crash inside
  ANGLE's `eglCreateDeviceANGLE` on H2 (Windows 11 build 26300, Intel Arc driver), so only the engine side
  of WindowsDX has a row for H2.
- **Firefox and other machines.** Only H2 and, for the one page it covered, H1.
- **Other graphics APIs and engines.** The D3D12, Vulkan and Metal backends and the other engines have no
  performance apps yet.
