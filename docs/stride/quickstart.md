# Stride quick start

Stride is the second engine this library supports that isn't MonoGame/KNI. Like raylib, it does not
go through `SkiaBackend`/`SkiaRenderer` - see
[SkiaStrideRenderTarget2D](../documentation/SkiaStrideRenderTarget2D.md) for why and how the Stride
support is structured differently.

## Prerequisites

- .NET 10
- Stride 4.4.0-beta5 or newer (prerelease - see "Known limitations" below)
- Windows and D3D11 only

## Add the package

```powershell
dotnet add package SkiaGameRendering.Stride.D3D11
```

## Getting a GraphicsCompositor without GameStudio

Stride's normal authoring flow is a GameStudio project with an asset pipeline, which is where a
`GraphicsCompositor` (camera, lighting, render stages) usually comes from. A pure-code `Game` like
`samples/Sample.Stride.D3D11` needs one from somewhere else - the
[Stride Community Toolkit](https://stride3d.github.io/stride-community-toolkit/)'s
`SetupBase3DScene()`/`AddSceneRenderer()` helpers are Stride's own documented way to do that without
GameStudio. This is a sample-only dependency; `SkiaGameRendering.Stride.D3D11` itself doesn't need it - any
project with its own `GraphicsCompositor` can skip the toolkit entirely.

## Initialize and render

Stride drives rendering through its `GraphicsCompositor`, not a user-owned `Draw()` call, so the
Skia draw runs inside a `SceneRendererBase.DrawCore` override -
[SkiaStrideSceneRenderer](../documentation/SkiaStrideRenderTarget2D.md) is that hook:

```cs
using SkiaGameRendering.Stride;
using SkiaSharp;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;

using var game = new Game();

SkiaStrideRenderTarget2D? canvas = null;
var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

// Not game.Run(...): Stride.Games.GameBase already declares an instance Run(GameContext) method,
// which hides the Stride.CommunityToolkit.Engine.GameExtensions.Run extension of the same name -
// call the extension directly to sidestep that.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start);

void Start(Scene rootScene)
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
    game.AddSceneRenderer(renderer); // draws on top of the 3D scene, every frame
}

canvas?.Dispose();
SkiaStrideRenderer.Dispose();
paint.Dispose();
```

Full member list and other remarks are documented on
[SkiaStrideRenderTarget2D](../documentation/SkiaStrideRenderTarget2D.md).

## Build and run the sample in this repo

```powershell
dotnet build samples\Sample.Stride.D3D11\Sample.Stride.D3D11.csproj -c Release
dotnet run --project samples\Sample.Stride.D3D11\Sample.Stride.D3D11.csproj -c Release --no-build
```

## Known limitations

- Windows and D3D11 only. Stride on Vulkan, Linux, or macOS is unsupported (Vulkan is a separate
  future adapter - repo issue #23).
- Depends on Stride 4.4.0-beta5, a prerelease, because that's the first version whose D3D11 backend
  exposes `GraphicsDevice.NativeDevice` publicly (see
  [SkiaStrideRenderTarget2D](../documentation/SkiaStrideRenderTarget2D.md#remarks)). Expect version
  churn until Stride 4.4 stabilizes.
- The sample builds and its reflection pins run in CI, but actual Skia-drawing-into-Stride-texture
  behavior needs a real window and D3D11 device to verify manually - it hasn't been run yet.
