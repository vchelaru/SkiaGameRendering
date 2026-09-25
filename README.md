# Skia Game Rendering

[![Join the chat](https://img.shields.io/discord/586997072373481494)](https://discord.gg/tG5RBgw)

A library that lets MonoGame, KNI, FNA, raylib, Stride, and Godot applications use SkiaSharp's GPU rendering to produce game-engine textures — with zero-copy GPU texture sharing. Skia renders anti-aliased vector art, text, and 2D graphics directly into game-engine textures without any CPU readback.

## Platform Support

| Platform | Backend | Status | How it works |
|----------|---------|--------|--------------|
| MonoGame 3.8.4 DesktopGL | OpenGL | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering)](https://www.nuget.org/packages/SkiaGameRendering) | Shared GL context via SDL |
| MonoGame 3.8.4 WindowsDX | D3D11 | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.WindowsDX)](https://www.nuget.org/packages/SkiaGameRendering.WindowsDX) | ANGLE (GL ES → D3D11 translation) on shared device |
| MonoGame 3.8.5 WindowsDX12 | D3D12 | Blocked — see `TODO.md` | |
| MonoGame 3.8.5 DesktopVK | Vulkan | Blocked — see `TODO.md` | |
| KNI DesktopGL | OpenGL | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.Kni.DesktopGL)](https://www.nuget.org/packages/SkiaGameRendering.Kni.DesktopGL) | Shared GL context via SDL |
| KNI WindowsDX | D3D11 | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.Kni.WindowsDX)](https://www.nuget.org/packages/SkiaGameRendering.Kni.WindowsDX) | ANGLE (GL ES → D3D11 translation) on shared device |
| KNI Android | GL ES | Not started | |
| KNI WebGL (Blazor) | WebGL2 | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.Kni.WebGL)](https://www.nuget.org/packages/SkiaGameRendering.Kni.WebGL) | Cross-context `texSubImage2D(canvas)` through KNI's stock public API |
| raylib | OpenGL | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.Raylib.OGL)](https://www.nuget.org/packages/SkiaGameRendering.Raylib.OGL) (Windows + Linux) | Second WGL (Windows) or GLX (Linux) context shares rlgl's GL namespace |
| FNA (D3D11) | D3D11 | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.Fna.WindowsDX)](https://www.nuget.org/packages/SkiaGameRendering.Fna.WindowsDX) (Windows) | ANGLE (GL ES → D3D11 translation) on the device FNA3D's D3D11 driver exposes through `FNA3D_GetSysRendererEXT`; needs `FNA3D_FORCE_DRIVER=D3D11` |
| FNA (OpenGL) | OpenGL | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.Fna.OGL)](https://www.nuget.org/packages/SkiaGameRendering.Fna.OGL) (Windows, Linux, macOS) | Second SDL GL context shared with FNA3D's; needs `FNA3D_FORCE_DRIVER=OpenGL` |
| FNA (SDL_GPU) | Vulkan/D3D12/Metal | Blocked: FNA3D's default driver exposes no native device (see the FNA section below) | |
| Stride (D3D11) | D3D11 | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.Stride.D3D11)](https://www.nuget.org/packages/SkiaGameRendering.Stride.D3D11) (Windows) | ANGLE (GL ES → D3D11 translation) on shared device |
| Stride (Vulkan) | Vulkan | [![NuGet](https://img.shields.io/nuget/v/SkiaGameRendering.Stride.VK)](https://www.nuget.org/packages/SkiaGameRendering.Stride.VK) (Linux, macOS, Windows) | Skia's Vulkan backend on Stride's shared `VkDevice`/`VkQueue`, no separate context |
| Godot 4.7+ (Vulkan) | Vulkan | Source only for now (`src/SkiaGameRendering.Godot`) | Skia's Vulkan backend on the handles Godot's `RenderingDevice.GetDriverResource` exposes publicly; drawn straight into an RD texture shown via `Texture2DRD`. No reflection. |
| Godot 4.7+ (D3D12) | D3D12 | Same package, backend picked at runtime (Windows) | Skia's D3D12 backend on Godot's `ID3D12Device`/queue; Skia draws into a typed resource this library owns and one GPU `CopyResource` per frame lands it in Godot's (typeless) texture - no CPU readback, but not zero-copy |
| Godot 4.7+ (Compatibility) | OpenGL 3.3 | Same package (Windows native WGL; Linux X11/GLX unrun) | Second GL context sharing Godot's, Skia draws into an FBO around an ordinary `ImageTexture`'s GL texture - zero-copy, the raylib adapter's shape |
| Godot 4 (macOS; Compatibility on ANGLE/EGL/Wayland) | Metal / Vulkan / OpenGL | Not started | Metal has no Core library here, SkiaSharp's macOS native build has no Vulkan backend (so MoltenVK is out), and the EGL/NSOpenGL-flavored GL contexts need platform code this repo does not have yet - see `TODO.md` |

MonoGame 3.8.5 ships the legacy `WindowsDX` (D3D11) project unchanged alongside the two new native
platforms above — only `WindowsDX12` and `DesktopVK` are blocked; D3D11 is expected to keep working
on 3.8.5 the same as it does on 3.8.4 (see `SkiaGameRendering-Notes.md` section 9).

## Requirements

- .NET 8 (.NET 10 for the Stride backend)
- Visual Studio 2022
- MonoGame 3.8.4.1+ (DesktopGL or WindowsDX; samples use 3.8.5.1), KNI (DesktopGL, WindowsDX, or WebGL/Blazor), FNA 26.09+ (D3D11 on Windows, or OpenGL anywhere), raylib, Stride 4.4.0-beta5+ (D3D11 on Windows, or Vulkan on Windows/Linux/macOS; prerelease), or Godot 4.7+ .NET (Forward+/Mobile on Vulkan or D3D12, or Compatibility on native OpenGL)
- SkiaSharp 3.119.4 for WebGL and the KNI desktop backends; 3.119.2 for the MonoGame desktop projects

The MonoGame DesktopGL (`SkiaGameRendering`) and WindowsDX (`SkiaGameRendering.WindowsDX`) packages
are trim- and NativeAOT-compatible; CI publishes `Sample.MonoGame.DesktopGL` (on Linux) and
`Sample.MonoGame.WindowsDX` (on Windows, WARP) with `PublishAot` and runs them. The other packages are not yet verified under NativeAOT.

## Quick Start

Install the NuGet package for your platform, then follow the setup for your engine below.

| Engine | Package | Full guide |
|--------|---------|------------|
| MonoGame DesktopGL | `SkiaGameRendering` | `docs/desktop/quickstart.md` |
| MonoGame WindowsDX | `SkiaGameRendering.WindowsDX` | `docs/desktop/quickstart.md` |
| KNI DesktopGL | `SkiaGameRendering.Kni.DesktopGL` | `docs/desktop/quickstart.md` |
| KNI WindowsDX | `SkiaGameRendering.Kni.WindowsDX` | `docs/desktop/quickstart.md` |
| KNI WebGL (Blazor) | `SkiaGameRendering.Kni.WebGL` | `docs/webgl/quickstart.md` (extra host setup) |
| FNA (D3D11) | `SkiaGameRendering.Fna.WindowsDX` | [FNA](#fna) |
| FNA (OpenGL) | `SkiaGameRendering.Fna.OGL` | [FNA](#fna) |
| raylib | `SkiaGameRendering.Raylib.OGL` | `docs/raylib/quickstart.md` |
| Stride (D3D11) | `SkiaGameRendering.Stride.D3D11` | `docs/stride/quickstart.md` |
| Stride (Vulkan) | `SkiaGameRendering.Stride.VK` | `docs/stride/vulkan-quickstart.md` |
| Godot (Vulkan, D3D12 or Compatibility) | `SkiaGameRendering.Godot` (not published yet; reference the project from source) | `docs/godot/quickstart.md` |

```powershell
dotnet add package <package from the table>
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
SkiaStrideRenderTarget2D? canvas = null;
var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

// Called as a static method because Game.Run(GameContext) hides the toolkit's Run extension.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: rootScene =>
{
    game.SetupBase3DScene();
    var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;
    canvas = new SkiaStrideRenderTarget2D(game.GraphicsDevice, backBuffer.Width, backBuffer.Height);

    var renderer = new SkiaStrideSceneRenderer { Canvas = canvas };
    renderer.SkiaDraw += skCanvas =>
    {
        skCanvas.Clear(SKColors.Transparent);
        skCanvas.DrawCircle(100, 100, 100, paint);
    };
    game.AddSceneRenderer(renderer); // draws on top of the 3D scene every frame
});

canvas?.Dispose();
SkiaStrideRenderer.Dispose();
paint.Dispose();
```

For Vulkan, use the `SkiaGameRendering.Stride.VK` namespace and the `SkiaStrideVulkan*` types
(`SkiaStrideVulkanRenderTarget2D`, `SkiaStrideVulkanSceneRenderer`, `SkiaStrideVulkanRenderer`).
On Windows, also set `<StrideGraphicsApi>Vulkan</StrideGraphicsApi>` in your project; see
`docs/stride/vulkan-quickstart.md`.

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

## Sample Projects

- `samples/Sample.MonoGame.DesktopGL/` — DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `samples/Sample.MonoGame.WindowsDX/` — WindowsDX sample (Windows only)
- `samples/Sample.Kni.DesktopGL/` — KNI DesktopGL sample (cross-platform: Windows, Linux, macOS)
- `samples/Sample.Kni.WindowsDX/` — KNI WindowsDX sample (Windows only)
- `samples/Sample.Kni.WebGL/` — KNI Blazor WebAssembly sample using the patched canvas-upload API
- `samples/Sample.Raylib.OGL/` — raylib sample (Windows + Linux)
- `samples/Sample.Fna.WindowsDX/`: FNA sample (Windows, D3D11 only; builds against the `external/FNA` submodule and the vendored `external/fnalibs`)
- `samples/Sample.Fna.OGL/`: FNA sample on FNA3D's OpenGL driver (same setup; the vendored fnalibs are Windows x64 only, so on Linux/macOS drop in your own)
- `samples/Sample.Stride.D3D11/` — Stride sample (Windows, D3D11 only)
- `samples/Sample.Stride.VK/` — Stride sample (Vulkan; builds on Windows via `StrideGraphicsApi=Vulkan`, runs on Windows/Linux/macOS)
- `samples/Sample.Godot/` — Godot 4.7 project (Vulkan, D3D12 or Compatibility via `--rendering-driver`; `dotnet build` it, then open or run it with a Godot .NET editor binary - not shipped here)
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
- `src/SkiaGameRendering.Kni.WebGL/` — KNI/Blazor library (shared core + `SkiaWebGlBackend`)
- `src/SkiaGameRendering.Fna.WindowsDX/`: FNA library (shared core + `SkiaFnaAngleBackend`, on `Core.ANGLE`, Windows/D3D11 only)
- `src/SkiaGameRendering.Fna.OGL/`: FNA library (shared core + `SkiaFnaGlBackend`, on `Core.OGL`; `src/SkiaGameRendering.Fna/` holds the FNA3D binding both FNA packages link)
- `src/SkiaGameRendering.Raylib.OGL/` — raylib library (shared `Core.OGL` + `SkiaRaylibRenderTarget2D`)
- `src/SkiaGameRendering.Stride.D3D11/` — Stride library (shared `Core.ANGLE` + `SkiaStrideRenderTarget2D`, Windows/D3D11 only)
- `src/SkiaGameRendering.Core.VK/` — engine-agnostic Vulkan/Skia interop shared by Vulkan-based backends
- `src/SkiaGameRendering.Stride.VK/` — Stride library (shared `Core.VK` + `SkiaStrideVulkanRenderTarget2D`, Windows/Linux/macOS)
- `src/SkiaGameRendering.Godot/` — Godot library (`Core.VK`, `Core.D3D12` and `Core.OGL` behind one `SkiaGodotRenderTarget2D`, backend chosen from the running driver; no reflection, all public Godot API)

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

The sample proves SpriteBatch interleaving, render-target consumption, shader sampling, animated Gum/Skia content, pointer/touch/wheel/text input, fractional DPR handling, and backend recreation. See `docs/webgl/quickstart.md`, `docs/webgl/validated-baseline.md`, and `docs/documentation/SkiaWebGlBackend.md` for the exact contract and support status.

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
