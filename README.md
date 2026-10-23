# Skia Game Rendering

[![Join the chat](https://img.shields.io/discord/586997072373481494)](https://discord.gg/tG5RBgw)

A library that lets MonoGame, KNI, FNA, raylib, Stride, Godot, and Unity applications use SkiaSharp's GPU rendering to produce game-engine textures — with zero-copy GPU texture sharing. Skia renders anti-aliased vector art, text, and 2D graphics directly into game-engine textures without any CPU readback.

## Platform Support

| Engine | Backend | Package | Platforms | Notes |
|--------|---------|---------|-----------|-------|
| [MonoGame 3.8.4 DesktopGL](docs/desktop/quickstart.md) | OpenGL | [![SkiaGameRendering](https://img.shields.io/nuget/v/SkiaGameRendering?label=SkiaGameRendering)](https://www.nuget.org/packages/SkiaGameRendering) | Windows, Linux, macOS |  |
| [MonoGame 3.8.4 WindowsDX](docs/desktop/quickstart.md) | D3D11 | [![SkiaGameRendering.WindowsDX](https://img.shields.io/nuget/v/SkiaGameRendering.WindowsDX?label=SkiaGameRendering.WindowsDX)](https://www.nuget.org/packages/SkiaGameRendering.WindowsDX) | Windows |  |
| [MonoGame 3.8.6 WindowsDX12](docs/desktop/quickstart.md) | D3D12 | [![SkiaGameRendering.WindowsDX12](https://img.shields.io/nuget/vpre/SkiaGameRendering.WindowsDX12?label=SkiaGameRendering.WindowsDX12)](https://www.nuget.org/packages/SkiaGameRendering.WindowsDX12) | Windows | Prerelease; needs prerelease MonoGame and `MonoGame.Runtime.Windows.DX12` |
| [MonoGame 3.8.6 DesktopVK](docs/desktop/quickstart.md) | Vulkan | [![SkiaGameRendering.DesktopVK](https://img.shields.io/nuget/vpre/SkiaGameRendering.DesktopVK?label=SkiaGameRendering.DesktopVK)](https://www.nuget.org/packages/SkiaGameRendering.DesktopVK) | Windows, Linux | Prerelease; needs prerelease MonoGame and `MonoGame.Runtime.Windows.Vulkan` or `MonoGame.Runtime.Linux.Vulkan`. **Stay on 0.17.4-preview (SkiaSharp 3.119.4).** Later releases use SkiaSharp 4.x, which needs Vulkan 1.1, and MonoGame creates its device at 1.0, so Skia fails on AMD and Mesa drivers. Not published until [MonoGame#9588](https://github.com/MonoGame/MonoGame/issues/9588) is fixed |
| [KNI DesktopGL](docs/desktop/quickstart.md) | OpenGL | [![SkiaGameRendering.Kni.DesktopGL](https://img.shields.io/nuget/v/SkiaGameRendering.Kni.DesktopGL?label=SkiaGameRendering.Kni.DesktopGL)](https://www.nuget.org/packages/SkiaGameRendering.Kni.DesktopGL) | Windows, Linux, macOS |  |
| [KNI WindowsDX](docs/desktop/quickstart.md) | D3D11 | [![SkiaGameRendering.Kni.WindowsDX](https://img.shields.io/nuget/v/SkiaGameRendering.Kni.WindowsDX?label=SkiaGameRendering.Kni.WindowsDX)](https://www.nuget.org/packages/SkiaGameRendering.Kni.WindowsDX) | Windows |  |
| [KNI Android](docs/android/quickstart.md) | OpenGL ES | [![SkiaGameRendering.Kni.Android](https://img.shields.io/nuget/v/SkiaGameRendering.Kni.Android?label=SkiaGameRendering.Kni.Android)](https://www.nuget.org/packages/SkiaGameRendering.Kni.Android) | Android 7+ (API 24), GLES 3.0+ expected (run on 3.2) | .NET 10; see the [quick start](docs/android/quickstart.md) for lifecycle notes |
| [KNI WebGL (Blazor)](docs/webgl/quickstart.md) | WebGL2 | [![SkiaGameRendering.Kni.WebGL](https://img.shields.io/nuget/v/SkiaGameRendering.Kni.WebGL?label=SkiaGameRendering.Kni.WebGL)](https://www.nuget.org/packages/SkiaGameRendering.Kni.WebGL) | Browser | Extra host setup; see [WebGL / WASM Status](#webgl--wasm-status) |
| [raylib](docs/raylib/quickstart.md) | OpenGL | [![SkiaGameRendering.Raylib.OGL](https://img.shields.io/nuget/v/SkiaGameRendering.Raylib.OGL?label=SkiaGameRendering.Raylib.OGL)](https://www.nuget.org/packages/SkiaGameRendering.Raylib.OGL) | Windows, Linux, macOS |  |
| [FNA](#fna) | D3D11 | [![SkiaGameRendering.Fna.WindowsDX](https://img.shields.io/nuget/v/SkiaGameRendering.Fna.WindowsDX?label=SkiaGameRendering.Fna.WindowsDX)](https://www.nuget.org/packages/SkiaGameRendering.Fna.WindowsDX) | Windows | Needs `FNA3D_FORCE_DRIVER=D3D11` |
| [FNA](#fna) | OpenGL | [![SkiaGameRendering.Fna.OGL](https://img.shields.io/nuget/v/SkiaGameRendering.Fna.OGL?label=SkiaGameRendering.Fna.OGL)](https://www.nuget.org/packages/SkiaGameRendering.Fna.OGL) | Windows, Linux, macOS | Needs `FNA3D_FORCE_DRIVER=OpenGL` |
| [Stride](docs/stride/quickstart.md) | D3D11 | [![SkiaGameRendering.Stride.D3D11](https://img.shields.io/nuget/v/SkiaGameRendering.Stride.D3D11?label=SkiaGameRendering.Stride.D3D11)](https://www.nuget.org/packages/SkiaGameRendering.Stride.D3D11) | Windows |  |
| [Stride](docs/stride/vulkan-quickstart.md) | Vulkan | [![SkiaGameRendering.Stride.VK](https://img.shields.io/nuget/v/SkiaGameRendering.Stride.VK?label=SkiaGameRendering.Stride.VK)](https://www.nuget.org/packages/SkiaGameRendering.Stride.VK) | Windows, Linux, macOS |  |
| [Stride](docs/stride/d3d12-quickstart.md) | D3D12 | [![SkiaGameRendering.Stride.D3D12](https://img.shields.io/nuget/v/SkiaGameRendering.Stride.D3D12?label=SkiaGameRendering.Stride.D3D12)](https://www.nuget.org/packages/SkiaGameRendering.Stride.D3D12) | Windows |  |
| [Godot 4.7+](docs/godot/quickstart.md) | Vulkan, D3D12, Metal, OpenGL 3.3 | `src/SkiaGameRendering.Godot` (source only, not on NuGet yet) | Windows, Linux, macOS | One package; backend picked at runtime. Compatibility/OpenGL on Linux (X11/GLX) is unrun |
| [Unity 6](#unity) | D3D11, D3D12, Metal | UPM git URL: `https://github.com/vchelaru/SkiaGameRendering.git#upm` (or `#upm/v<version>` to pin) | Windows x64 (Mono, IL2CPP), macOS | Not on NuGet |

Not supported yet:

- **MonoGame Android**: not started (KNI Android works).
- **FNA SDL_GPU** (Vulkan/D3D12/Metal): blocked, FNA3D's default driver exposes no native device (see [FNA](#fna)).
- **Godot Vulkan on macOS, and Compatibility on ANGLE/EGL/Wayland**: SkiaSharp's macOS native build has no Vulkan backend (so MoltenVK is out), and the EGL/NSOpenGL-flavored GL contexts need platform code this repo does not have yet. See `TODO.md`.

MonoGame ships the legacy `WindowsDX` (D3D11) project unchanged alongside the two native platforms
above. 3.8.5 hid the native platforms' graphics device, which blocked them; 3.8.6-preview.2 added
`GraphicsDevice.GetNativeHandles()` and `RenderTarget2D.FromNativeHandle()`, which the `WindowsDX12`
and `DesktopVK` backends use (see `SkiaGameRendering-Notes.md` section 9). A game using them also
references the native runtime package for its platform (`MonoGame.Runtime.Windows.DX12`,
`MonoGame.Runtime.Windows.Vulkan` or `MonoGame.Runtime.Linux.Vulkan`).

## Requirements

- .NET 8 (.NET 10 for the Stride and Android backends)
- Visual Studio 2022
- MonoGame 3.8.4.1+ (DesktopGL or WindowsDX; samples use 3.8.5.1), MonoGame 3.8.6-preview.2+ (native WindowsDX12 or DesktopVK), KNI (DesktopGL, WindowsDX, WebGL/Blazor, or Android), FNA 26.09+ (D3D11 on Windows, or OpenGL anywhere), raylib, Stride 4.4.0-beta5+ (D3D11 or D3D12 on Windows, or Vulkan on Windows/Linux/macOS; prerelease), or Godot 4.7+ .NET (Forward+/Mobile on Vulkan, D3D12 or Metal, or Compatibility on native OpenGL)
- SkiaSharp 4.153.1 (3.119.4 for `SkiaGameRendering.DesktopVK` 0.17.4-preview)

The MonoGame DesktopGL (`SkiaGameRendering`) and WindowsDX (`SkiaGameRendering.WindowsDX`) packages
are trim- and NativeAOT-compatible; CI publishes `Sample.MonoGame.DesktopGL` (on Linux) and
`Sample.MonoGame.WindowsDX` (on Windows, WARP) with `PublishAot` and runs them. The other packages are not yet verified under NativeAOT.

## Quick Start

Install the NuGet package for your platform, then follow the setup for your engine below.

The Package column in the [table above](#platform-support) is the NuGet name, and the Engine column links each engine's full guide.

```powershell
dotnet add package <package from the table>  # add --prerelease for the prerelease packages
```

### MonoGame, KNI, and FNA

These share one API (`SkiaRenderer` plus `SkiaRenderTarget2D`), so the code inside `Game` is
identical on all of them. `Program.cs` stays whatever the stock template gives you, except on FNA,
which needs one extra line to pick its graphics driver (see [FNA](#fna)).

Inside `Game`, poll `SkiaRenderer.IsReady` before calling `SkiaRenderer.Initialize`, in `Draw()`:
```cs
using SkiaGameRendering; // SkiaRenderer

protected override void Draw(GameTime gameTime)
{
    if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
        SkiaRenderer.Initialize(GraphicsDevice);

    if (SkiaRenderer.IsInitialized)
    {
        // normal draw logic
    }
    base.Draw(gameTime);
}
```

`IsReady` is always `true` on desktop. On KNI WebGL it reflects a real async host-readiness check,
which is why the same code works there unchanged (see `docs/webgl/quickstart.md`). `Game` code never
names a specific `SkiaBackend` type on any platform. Drawing goes through
[SkiaRenderTarget2D](#skiarendertarget2d).

### raylib

raylib has no `Game` class, so it gets its own `SkiaRaylibRenderTarget2D` that you drive from the
main loop, between `BeginDrawing`/`EndDrawing` like any other raylib draw call. On Linux, also add
`SkiaSharp.NativeAssets.Linux`.

```cs
using Raylib_cs;
using SkiaGameRendering.Raylib.OGL;
using SkiaSharp;

Raylib.InitWindow(800, 600, "raylib + Skia");
var canvas = new SkiaRaylibRenderTarget2D(800, 600);
using var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

while (!Raylib.WindowShouldClose())
{
    Raylib.BeginDrawing();
    canvas.Begin();
    canvas.Canvas.DrawCircle(100, 100, 100, paint);
    canvas.End(); // composites onto the screen at (0,0)
    Raylib.EndDrawing();
}

canvas.Dispose();
SkiaRaylibRenderer.Dispose();
Raylib.CloseWindow();
```

### Stride

Stride renders through its `GraphicsCompositor` rather than a user-owned `Draw()`, so you add a
`SkiaStrideSceneRenderer` to the compositor and draw in its `SkiaDraw` event. This example uses the
[Stride Community Toolkit](https://stride3d.github.io/stride-community-toolkit/) to get a compositor
without GameStudio; the package itself doesn't depend on it.

```cs
using SkiaGameRendering.Stride.D3D11;
using SkiaSharp;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;

using var game = new Game();
SkiaStrideDirectCanvas? canvas = null;
var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

// Called as a static method because Game.Run(GameContext) hides the toolkit's Run extension.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: rootScene =>
{
    game.SetupBase3DScene();
    canvas = new SkiaStrideDirectCanvas(game.GraphicsDevice);

    var renderer = new SkiaStrideSceneRenderer { DirectCanvas = canvas };
    renderer.SkiaDraw += skCanvas => skCanvas.DrawCircle(100, 100, 100, paint);
    game.AddSceneRenderer(renderer); // draws on top of the 3D scene every frame
});

canvas?.Dispose();
SkiaStrideRenderer.Dispose();
paint.Dispose();
```

`DirectCanvas` draws straight into the back buffer (see
[Drawing straight into the current target](#drawing-straight-into-the-current-target)). To draw into
a texture and composite it instead, set `Canvas` to a `SkiaStrideRenderTarget2D` sized to the back
buffer and clear it in `SkiaDraw`.

For Vulkan, use the `SkiaGameRendering.Stride.VK` namespace and the `SkiaStrideVulkan*` types
(`SkiaStrideVulkanRenderTarget2D`, `SkiaStrideVulkanSceneRenderer`, `SkiaStrideVulkanRenderer`).
Those have no direct canvas yet, so set the scene renderer's `Canvas`.
On Windows, also set `<StrideGraphicsApi>Vulkan</StrideGraphicsApi>` in your project; see
`docs/stride/vulkan-quickstart.md`.

For Direct3D 12, use the `SkiaGameRendering.Stride.D3D12` namespace and the `SkiaStrideD3D12*` types,
and set `<StrideGraphicsApi>Direct3D12</StrideGraphicsApi>`; see `docs/stride/d3d12-quickstart.md`.
`SkiaStrideD3D12DirectCanvas` and `SkiaStrideD3D12SceneRenderer.DirectCanvas` work like the D3D11 ones above.

### Unity

Unity renders on its own thread, so the canvas records your drawing and `End` plays it back on
Unity's render thread. Call `Begin`/`End` from the main thread. Skia writes premultiplied alpha,
so draw `Texture` with `SkiaUnityRenderTarget.PremultipliedMaterial` (a `RawImage`'s material, or
a copy on a mesh) or, for `Graphics.DrawTexture` in `OnGUI`, `PremultipliedGuiMaterial`; Unity's
default blending darkens its edges. Both work in Gamma and Linear color space projects, but only
with the right one for where you draw, since IMGUI stays in gamma in a Linear project. Supported so far: Direct3D 11 on Windows (set Player Settings > Other Settings > Graphics APIs
for Windows to Direct3D11) and Metal on macOS, the default there. The package only compiles for the
Editor and Windows x64 and macOS players, so code that uses it needs the same limits, or the
project's other platform builds fail to compile. Put that code in an asmdef limited to Editor,
Windows 64-bit and macOS, or inside
`#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX || (UNITY_STANDALONE_WIN && UNITY_64) || UNITY_STANDALONE_OSX`.

```csharp
var target = new SkiaUnityRenderTarget(512, 512);

void Update()
{
    target.Begin();
    target.Canvas.DrawCircle(256, 256, 200, paint);
    target.End();
}

void OnGUI()
{
    if (Event.current.type == EventType.Repaint)
        Graphics.DrawTexture(new Rect(0, 0, 512, 512), target.Texture, SkiaUnityRenderTarget.PremultipliedGuiMaterial);
}
```

## SkiaRenderTarget2D

Applies to MonoGame, KNI, and FNA. raylib and Stride have their own render-target types with the
same `Begin`/`Canvas`/`End` shape (see their quickstarts).

`SkiaRenderTarget2D` is a GPU surface that SkiaSharp renders directly into, sized to match whatever
you intend to draw it onto (typically the back buffer, or a `RenderTarget2D` the same size as the
viewport). Its Begin/End shape works like `SpriteBatch`'s own: place individual shapes with their
own coordinates via Skia's drawing API — the same way a `SpriteBatch.Draw` call carries its own
position — and `End()` composites the whole result onto whatever render target is currently bound,
the same way `SpriteBatch.End()` needs no separate step to show its queued sprite draws:

```cs
using SkiaGameRendering; // SkiaRenderTarget2D
using SkiaSharp;          // SKPaint and the rest of the drawing API

var canvas = new SkiaRenderTarget2D(GraphicsDevice, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);

// per frame:
canvas.Begin();
canvas.Canvas.DrawCircle(100, 100, 100, paint); // position is DrawCircle's job, not End's
canvas.End();
```

| Member | Description |
|--------|-------------|
| `Texture` | The underlying `Texture2D`, mainly useful with `EndWithoutDrawing` |
| `Canvas` | The `SKCanvas` to draw on — only valid between `Begin()` and `End()`; throws otherwise |
| `Begin(bool clear = true)` | Starts a render pass; throws if called again before `End()` |
| `End()` | Ends the render pass and composites the whole surface, at native size and the origin, onto whatever's currently bound; throws if `Begin()` wasn't called first |
| `EndWithoutDrawing()` | Same as `End()`, but skips the composite — use only when `End()`'s single whole-surface blit can't express what you need (drawing it more than once, at a different size/position, or sampling it in a shader). You then draw `Texture` yourself. |
| `Dispose()` | Releases the underlying GPU resources; throws if called between `Begin()` and `End()` |

Size is fixed for the object's lifetime, like `RenderTarget2D` — construct a new
`SkiaRenderTarget2D` (and `Dispose()` the old one) if you need a different size. Set a render
target before calling `Begin()` if you want `End()`'s composite to land somewhere other than the
back buffer.

Tear the shared backend down (e.g. on exit, or before switching backends) with:
```cs
SkiaRenderer.Dispose();
```
Dispose your own `SkiaRenderTarget2D` instances first — this doesn't track or dispose them for you.

## Drawing straight into the current target

On MonoGame DesktopGL, KNI DesktopGL, KNI Android, MonoGame WindowsDX, KNI WindowsDX and FNA (on its
D3D11 or OpenGL driver), `SkiaDirectCanvas` draws into whatever the
engine is rendering to when `Begin()` is called, the back buffer or the bound `RenderTarget2D`, with
no intermediate texture and no `SpriteBatch` composite. It has the same `Begin`/`Canvas`/`End` shape as
`SkiaRenderTarget2D`, without the size or the texture:

```cs
_skia = new SkiaDirectCanvas(GraphicsDevice); // once, e.g. in LoadContent

spriteBatch.Begin(); /* background */ spriteBatch.End();
_skia.Begin();
_skia.Canvas.DrawCircle(100, 100, 50, paint);
_skia.End();
spriteBatch.Begin(); /* foreground */ spriteBatch.End();
```

`Begin()` does not clear; Skia draws on top of what is there. Coordinates are target pixels from the
top left; the engine's `Viewport` and scissor rectangle are ignored. Multisampled back buffers and render
targets work. Only one bound `RenderTarget2D` with `SurfaceFormat.Color` is supported; on the D3D11
platforms a back buffer whose texture has an sRGB format is refused too. `SkiaRenderer.SupportsDirectCanvas`
is false on every other platform, where the constructor throws `NotSupportedException`; use
`SkiaRenderTarget2D` there.

Stride on Direct3D 11 has the same thing as `SkiaStrideDirectCanvas`. `Begin` takes the
`GraphicsContext`, where Stride keeps its current target, and `End` takes nothing; or set
`SkiaStrideSceneRenderer.DirectCanvas` (see the Stride quick start). Inside the compositor that target
is the back buffer. In Stride's default Linear color space the back buffer is an sRGB view of an
8-bit texture: Skia writes into it without encoding, so an `SKColor` shows as that color, and Skia
blends in sRGB space while Stride blends in linear. One bound 8-bit RGBA or BGRA target is supported;
sRGB-format textures and HDR targets throw `NotSupportedException`. Stride's Vulkan backend has no
direct canvas yet.

Stride on Direct3D 12 has `SkiaStrideD3D12DirectCanvas`, used the same way, with the same color
handling, and with single-sample targets only (a multisampled one throws `NotSupportedException`).
`Begin` submits whatever Stride has recorded on the command list so far and starts a new recording,
because Skia's work reaches the GPU when `End` runs and would otherwise land under Stride's earlier
draws. That makes each pass cost a submission, so draw it once per frame rather than once per widget.
MonoGame's native WindowsDX12 has no direct canvas: its managed API and its native interop table give
no way to get the current target's `ID3D12Resource`, or to submit what MonoGame has recorded so far, so
Skia could neither find the target nor draw after the engine's earlier work. Use `SkiaRenderTarget2D` there.

## Performance

Skia is a general-purpose vector renderer. It is not a sprite batcher, so draw sprites with the
engine's own `SpriteBatch` and use Skia for vectors, SVG and text. The two interleave freely.

- **If sprites must go through Skia, use `SKCanvas.DrawAtlas`.** One `DrawAtlas` call per texture
  sheet costs about the same as `SpriteBatch`. One `DrawImage` per sprite is several times slower.
- **Tint through `DrawAtlas`'s `colors` array with `SKBlendMode.Modulate`**, the equivalent of
  `SpriteBatch.Draw`'s `color`. It costs almost nothing extra. Tinting `DrawImage` with a per-sprite
  `SKPaint.ColorFilter` is the slowest option measured. `SKColor` is unpremultiplied, so the matching
  `SpriteBatch` color is `Color.FromNonPremultiplied(r, g, b, a)`.
- **Upload sprite images once** with `SKImage.ToTextureImage(SkiaRenderer.CurrentBackend.GRContext)`,
  called between `Begin()` and `End()` when Skia's GPU context is current, and reuse the result.
- **Use few, large targets.** Every `Begin()`/`End()` pair switches GPU context and flushes Skia. On
  WindowsDX and KNI WindowsDX, `End()` also calls `glFinish`, which stalls the CPU until the GPU
  finishes; `SkiaDirectCanvas.End()` does not. One screen-sized target per frame beats one per element.

Measured with `benchmarks/Benchmarks.ShapeRendering.DesktopGL` (MonoGame DesktopGL, macOS, Apple
Silicon), frame time in ms:

| Sprites | Skia `DrawImage` | Skia `DrawAtlas` | `SpriteBatch` |
|---:|---:|---:|---:|
| 0 | 0.190 | 0.187 | 0.149 |
| 500 | 0.466 | 0.242 | 0.174 |
| 2k | 1.017 | 0.284 | 0.218 |
| 10k | 3.977 | 0.638 | 0.579 |
| 500 tinted | 0.836 | 0.250 | 0.174 |
| 10k tinted | 10.794 | 0.658 | 0.585 |

`SkiaDirectCanvas` saves the full-screen clear and composite of a `SkiaRenderTarget2D`, about
8-12% of the Skia frame cost for a 450-draw scene on an AMD Radeon 780M
(`benchmarks/Benchmarks.DirectDraw.DesktopGL`).

For whole-frame FPS of a plain MonoGame app against the same scene drawn through Skia, by platform
and hardware, see [docs/performance.md](docs/performance.md).

## Sample Projects

- `samples/Sample.MonoGame.DesktopGL/` — DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `samples/Sample.MonoGame.WindowsDX/` — WindowsDX sample (Windows only)
- `samples/Sample.Kni.DesktopGL/` — KNI DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `samples/Sample.Kni.WindowsDX/` — KNI WindowsDX sample (Windows only)
- `samples/Sample.Kni.Android/`: KNI Android sample (.NET 10; install the APK, or run with `--ez smoke true` to check the pixels)
- `samples/Sample.Kni.WebGL/` — KNI Blazor WebAssembly sample
- `samples/Sample.Gum.Kni.WebGL/` — KNI Blazor WebAssembly sample of Gum UI interleaved with SpriteBatch, render targets, shaders, and browser input
- `samples/Sample.Raylib.OGL/` — raylib sample (Windows, Linux, macOS)
- `samples/Sample.Fna.WindowsDX/`: FNA sample (Windows, D3D11 only; builds against the `external/FNA` submodule and the vendored `external/fnalibs`)
- `samples/Sample.Fna.OGL/`: FNA sample on FNA3D's OpenGL driver (same setup; the vendored fnalibs are Windows x64 only, so on Linux/macOS drop in your own)
- `samples/Sample.Stride.D3D11/` — Stride sample (Windows, D3D11 only)
- `samples/Sample.Stride.VK/` — Stride sample (Vulkan; builds on Windows via `StrideGraphicsApi=Vulkan`, runs on Windows/Linux/macOS)
- `samples/Sample.Stride.D3D12/`: Stride sample (Windows, D3D12 via `StrideGraphicsApi=Direct3D12`)
- `samples/Sample.Godot/` — Godot 4.7 project (Vulkan, D3D12 or Compatibility via `--rendering-driver`; `dotnet build` it, then open or run it with a Godot .NET editor binary - not shipped here)
- `samples/Sample.Unity/`: Unity 6 project (Windows D3D11 and D3D12, macOS Metal). Run `eng/build-unity-package.ps1` first to fill the package's `Plugins/`; `--smoke-test` checks the rendered pixels and exits
- `samples/Test/` — More comprehensive test with dynamic add/remove, FPS counter, input handling

DesktopGL, WindowsDX, KNI WindowsDX, and both FNA samples share the same `Game1.cs` via a linked file include; KNI DesktopGL has its own copy.

## Architecture

The library uses a backend abstraction (`SkiaBackend` base class) so each graphics API gets its own implementation. Core source files are shared across platform-specific library projects via linked includes:

- `src/SkiaGameRendering/` — DesktopGL library (core + `SkiaGlBackend`)
- `src/SkiaGameRendering.Core.OGL/` — engine-agnostic raw-GL/Skia FBO interop shared by GL-based backends
- `src/SkiaGameRendering.Core.ANGLE/` — engine-agnostic D3D11/ANGLE interop shared by ANGLE-based backends
- `src/SkiaGameRendering.WindowsDX/` — MonoGame WindowsDX library (shared core + `SkiaAngleBackend`, on `Core.ANGLE`)
- `src/SkiaGameRendering.Kni.DesktopGL/` — KNI DesktopGL library (shared core + `SkiaKniGlBackend`)
- `src/SkiaGameRendering.Kni.WindowsDX/` — KNI WindowsDX library (shared core + `SkiaKniAngleBackend`, on `Core.ANGLE`)
- `src/SkiaGameRendering.Kni.Android/`: KNI Android library (shared core + `SkiaKniAndroidBackend`, on `Core.OGL`'s `EglSharedContext`; targets `net10.0-android36.0`)
- `src/SkiaGameRendering.Kni.WebGL/` — KNI/Blazor library (shared core + `SkiaWebGlBackend`)
- `src/SkiaGameRendering.Fna.WindowsDX/`: FNA library (shared core + `SkiaFnaAngleBackend`, on `Core.ANGLE`, Windows/D3D11 only)
- `src/SkiaGameRendering.Fna.OGL/`: FNA library (shared core + `SkiaFnaGlBackend`, on `Core.OGL`; `src/SkiaGameRendering.Fna/` holds the FNA3D binding both FNA packages link)
- `src/SkiaGameRendering.Raylib.OGL/` — raylib library (shared `Core.OGL` + `SkiaRaylibRenderTarget2D`)
- `src/SkiaGameRendering.Stride.D3D11/` — Stride library (shared `Core.ANGLE` + `SkiaStrideRenderTarget2D`, Windows/D3D11 only)
- `src/SkiaGameRendering.Core.VK/` — engine-agnostic Vulkan/Skia interop shared by Vulkan-based backends
- `src/SkiaGameRendering.Core.Metal/`: engine-agnostic Metal/Skia interop shared by Metal-based backends (Godot on macOS today)
- `src/SkiaGameRendering.Stride.VK/` — Stride library (shared `Core.VK` + `SkiaStrideVulkanRenderTarget2D`, Windows/Linux/macOS)
- `src/SkiaGameRendering.Core.D3D12/`: engine-agnostic D3D12/Skia interop shared by D3D12-based backends
- `src/SkiaGameRendering.Stride.D3D12/`: Stride library (shared `Core.D3D12` + `SkiaStrideD3D12RenderTarget2D`, Windows/D3D12 only)
- `unity/com.vchelaru.skiagamerendering/`: Unity UPM package (`Core.ANGLE`'s, `Core.D3D12`'s and `Core.Metal`'s netstandard2.1 builds + `SkiaUnityRenderTarget`, Windows/D3D11, Windows/D3D12 and macOS/Metal; the plugins' sources are `unity/native/SkiaUnityD3D12/` and `unity/native/SkiaUnityMetal/`)
- `src/SkiaGameRendering.Godot/` — Godot library (`Core.VK`, `Core.D3D12`, `Core.Metal` and `Core.OGL` behind one `SkiaGodotRenderTarget2D`, backend chosen from the running driver; no reflection, all public Godot API)

### How each backend works

| Backend | Technique |
|---------|-----------|
| OpenGL (MonoGame/KNI DesktopGL, FNA OpenGL) | Second SDL GL context shared with the engine's |
| OpenGL ES (KNI Android) | Second EGL context sharing KNI's, created from the host's current `EGLConfig`; surfaceless where the driver allows it |
| OpenGL (raylib) | Second WGL (Windows), GLX (Linux) or CGL (macOS) context shares rlgl's GL namespace |
| OpenGL 3.3 (Godot Compatibility) | Second GL context sharing Godot's; Skia draws into an FBO around an `ImageTexture`'s GL texture, zero-copy |
| D3D11 (MonoGame/KNI/FNA WindowsDX, Stride, Unity) | ANGLE (GL ES → D3D11 translation) on the engine's shared device. FNA gets the device through `FNA3D_GetSysRendererEXT`; Unity uses the device its `RenderTexture` belongs to |
| D3D12 (MonoGame WindowsDX12, Stride, Godot, Unity) | Skia's D3D12 backend on the engine's `ID3D12Device`/queue. MonoGame wraps the target with `RenderTarget2D.FromNativeHandle()`; Godot draws into a typed resource and does one GPU `CopyResource` per frame (no readback, not zero-copy); Unity goes through a small native plugin |
| Vulkan (MonoGame DesktopVK, Stride, Godot) | Skia's Vulkan backend on the engine's `VkDevice`/`VkQueue`, no separate context. Godot's handles come from public `RenderingDevice.GetDriverResource`, no reflection |
| Metal (Godot, Unity) | Skia's Metal backend on the engine's `MTLDevice`/queue, drawn straight into the engine's `MTLTexture`; Unity goes through a small native plugin |
| WebGL2 (KNI Blazor) | Cross-context `texSubImage2D(canvas)` through KNI's stock public API |

Unity records draws to an `SKPicture` on the main thread and plays them back on its render thread.

See `SkiaGameRendering-Notes.md` for detailed technical documentation on how each backend works, including the ANGLE integration and D3D11 state management.

## FNA

FNA's graphics layer is the native FNA3D library, which has three drivers: SDL_GPU (the default on
SDL3 builds), D3D11 (Windows) and OpenGL. Only D3D11 and OpenGL hand out their native device
(`FNA3D_GetSysRendererEXT`); the SDL_GPU driver leaves that call unimplemented, and SDL3 itself
exposes no native handles from an `SDL_GPUDevice`, so there is nothing for Skia to share. Until
that changes upstream, the FNA backends need FNA3D's D3D11 or OpenGL driver, which you select with
an SDL hint before the `Game` exists:

```csharp
// Program.cs
System.Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "D3D11"); // or "OpenGL" with SkiaGameRendering.Fna.OGL
using var game = new Game1();
game.Run();
```

`SkiaRenderer.Initialize` throws with that instruction if the package's driver isn't the active one.
D3D11 goes through ANGLE like the MonoGame/KNI WindowsDX backends; OpenGL creates a second SDL GL
context sharing FNA's, like MonoGame DesktopGL, and works wherever FNA3D's GL driver does. Everything
inside `Game` is the same code as the MonoGame/KNI backends (`samples/Sample.Fna.WindowsDX` links
the shared `Game1.cs`). FNA itself is referenced the FNA way, as a submodule plus a
`ProjectReference`, so the package carries no FNA dependency and binds to whatever FNA your game
builds against; ship `fnalibs` next to your exe as usual. Tested against FNA 26.09.

## WebGL / WASM Status

The WebGL backend is implemented in `SkiaGameRendering.Kni.WebGL`. It creates a synchronous SkiaSharp WebGL2 source host, flushes the current Skia frame, and uploads that canvas directly into a preallocated KNI `Texture2D` with `texSubImage2D`. Production code has no `readPixels`, managed pixel buffer, or `Texture2D.SetData(byte[])` path.

The browser backend consumes stock KNI from NuGet (no fork or patch); the one internal (the current WebGL rendering context) that KNI doesn't expose publicly is reached via reflection instead — see `src/SkiaGameRendering.Kni.WebGL/WebGlCanvasUpload.cs`.

```powershell
dotnet workload install wasm-tools-net8
dotnet build samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release
dotnet run --project samples\Sample.Kni.WebGL\Sample.Kni.WebGL.csproj -c Release --no-build
```

`Sample.Kni.WebGL` draws the shared scene like every other sample. `Sample.Gum.Kni.WebGL` (same commands, different project) covers SpriteBatch interleaving, render-target consumption, shader sampling, animated Gum content, pointer/touch/wheel/text input, fractional DPR handling, and backend recreation. See `docs/webgl/quickstart.md`, `docs/webgl/validated-baseline.md`, and `docs/documentation/SkiaWebGlBackend.md` for the exact contract and support status.

### Firefox is not usable yet

Measured on real hardware (`docs/webgl/performance-results.md`), every upload path misses budget on
Firefox by 60-300x (35-171ms per frame just for the upload, vs. a <1ms target), consistent with an
internal CPU readback on cross-context canvas uploads. Chrome and Edge are unaffected (Tier 1).
Fixing this needs a shared-GL-context redesign ("Option A" in `docs/webgl/validated-baseline.md`),
which is unstarted.

## Using SkiaSharp

Between `Begin()` and `End()`, `SkiaRenderTarget2D.Canvas` gives you a full GPU-accelerated
`SKCanvas`. For example, drawing an anti-aliased circle:

```cs
canvas.Begin();
canvas.Canvas.DrawCircle(Radius, Radius, Radius, _paint);
canvas.End();
```

For more on SkiaSharp drawing, see the [SkiaSharp documentation](https://learn.microsoft.com/en-us/previous-versions/xamarin/xamarin-forms/user-interface/graphics/skiasharp/basics/).

## License

MIT License. See [LICENSE.md](LICENSE.md).

## Credits

Originally created by [Miguel Anxo Figueirido](https://github.com/mfigueirido/SkiaMonoGameRendering). Multi-platform backend abstraction and WindowsDX/ANGLE support added by Victor Chelaru.
