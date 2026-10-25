# Stride Vulkan quick start

Vulkan analog of `docs/stride/quickstart.md` (D3D11). Like that adapter, this does not go through
`SkiaBackend` - see
[SkiaRenderTarget2D (Stride Vulkan)](../documentation/SkiaRenderTarget2D.md#stride-vulkan) for why and how
the Stride Vulkan support is structured.

## Prerequisites

- .NET 10
- Stride 4.4.1 or newer
- Windows, Linux, or macOS, with a Vulkan 1.3-capable driver (Stride itself requires 1.3's dynamic
  rendering and synchronization2, plus 1.2's timeline semaphores - it hard-throws on device creation
  otherwise)

## Add the package

```powershell
dotnet add package SkiaGameRendering.Stride.VK
```

## Building for Vulkan on Windows

Stride picks its graphics API per-project via the `StrideGraphicsApi` MSBuild property, defaulting to
Direct3D11 on Windows and Vulkan on Linux/macOS. To build (and run) the Vulkan path from a Windows
dev box - useful for CI, or for exercising this adapter without Linux/macOS hardware - force it
explicitly in your project:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework> <!-- not net10.0-windows -->
  <StrideGraphicsApi>Vulkan</StrideGraphicsApi>
</PropertyGroup>
```

`samples/Sample.Stride.VK` does exactly this - see its `.csproj`.

## Getting a GraphicsCompositor without GameStudio

Same story as the D3D11 sample: a pure-code `Game` needs a `GraphicsCompositor` from somewhere other
than GameStudio's asset pipeline. The [Stride Community Toolkit](https://stride3d.github.io/stride-community-toolkit/)'s
`Run` (which applies a default `SceneSetup3D`) and `AddSceneRenderer()` helpers provide one without it. This is a sample-only
dependency; `SkiaGameRendering.Stride.VK` itself doesn't need it.

## Initialize and render

Stride drives rendering through its `GraphicsCompositor`, not a user-owned `Draw()` call, so the
Skia draw runs inside a `SceneRendererBase.DrawCore` override -
[SkiaSceneRenderer](../documentation/SkiaRenderTarget2D.md#stride-vulkan) is that hook:

```cs
using SkiaGameRendering;
using SkiaSharp;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;

using var game = new Game();

SkiaRenderTarget2D? canvas = null;
var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

// Not game.Run(...): Stride.Games.GameBase already declares an instance Run(GameContext) method,
// which hides the Stride.CommunityToolkit.Engine.GameExtensions.Run extension of the same name -
// call the extension directly to sidestep that.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start);

void Start(Scene rootScene)
{

    var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;
    canvas = new SkiaRenderTarget2D(game.GraphicsDevice, backBuffer.Width, backBuffer.Height);

    var renderer = new SkiaSceneRenderer { Canvas = canvas };
    renderer.SkiaDraw += skCanvas =>
    {
        skCanvas.Clear(SKColors.Transparent);
        skCanvas.DrawCircle(100, 100, 100, paint);
    };
    game.AddSceneRenderer(renderer); // draws on top of the 3D scene, every frame
}

canvas?.Dispose();
SkiaRenderer.Dispose();
paint.Dispose();
```

Full member list and other remarks are documented on
[SkiaRenderTarget2D (Stride Vulkan)](../documentation/SkiaRenderTarget2D.md#stride-vulkan).

## Build (and run) the sample in this repo

```powershell
dotnet build samples\Sample.Stride.VK\Sample.Stride.VK.csproj -c Release
dotnet run --project samples\Sample.Stride.VK\Sample.Stride.VK.csproj -c Release --no-build
```

`--smoke-test` (through `SkiaRenderTarget2D`) and `--direct-smoke-test` (through `SkiaSceneRenderer.DirectCanvas`) render a few frames, check the back buffer for the shared scene, and exit with 0 or 1.

The build step works on any platform, including a Windows box with no discrete Vulkan-capable GPU at
all - forcing `StrideGraphicsApi=Vulkan` only picks which `Stride.Graphics.dll` variant gets compiled
against and copied to output, it does not require a Vulkan driver to be present. Actually **running**
it needs a real Vulkan 1.3 driver on whatever machine you run it on (see "Known limitations").

## Known limitations

- Depends on Stride 4.4.1 or newer. Earlier 4.4 builds have a missing `COLOR_ATTACHMENT_READ` access in the render target barrier (a synchronization hazard when a pass loads the target); 4.4.1 fixes it.
- **sRGB swap chain.** In Stride's default Linear color space the Vulkan back buffer is an sRGB-format image. Skia cannot wrap an sRGB-format `VkImage`, so the library wraps it as its UNORM twin and an `SKColor` lands as that color (Stride D3D11 and D3D12 refuse sRGB targets instead). Stride creates the swap chain without `VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT`, so viewing it as UNORM is outside the letter of the Vulkan spec. Drivers accept it for same-size formats (checked on an NVIDIA GPU). CI runs `StrideVulkanLayoutValidationTests` under the validation layer and tolerates only that one message ([#304](https://github.com/vchelaru/SkiaGameRendering/issues/304), upstream [stride#3695](https://github.com/stride3d/stride/issues/3695)). If a driver or validation layer rejects the wrapped back buffer, draw through the render-target path instead: a `SkiaRenderTarget2D` set as the `Canvas` of a `SkiaSceneRenderer` composites onto the back buffer and never touches that image.
- The sample builds and its reflection pins run in CI on Windows (with `StrideGraphicsApi=Vulkan`
  forced), but actual Skia-drawing-into-Stride-texture behavior needs a real window and a Vulkan 1.3
  driver to verify manually - it hasn't been run yet. See the PR that introduced this adapter (repo
  issue #54) for the exact manual verification steps and what platform/GPU they need.
