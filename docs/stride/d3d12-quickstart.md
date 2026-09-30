# Stride Direct3D 12 quick start

Direct3D 12 analog of `docs/stride/quickstart.md` (D3D11). Skia's own D3D12 backend draws on
Stride's `ID3D12Device` and direct queue, straight into the Stride `Texture`, with no ANGLE layer.

## Prerequisites

- .NET 10
- Stride 4.4.0-beta5 or newer (prerelease)
- Windows with a Direct3D 12 GPU (WARP, the software rasterizer, also works)

## Add the package

```powershell
dotnet add package SkiaGameRendering.Stride.D3D12
```

Stride picks its graphics API per project, defaulting to Direct3D 11 on Windows, so select D3D12 in
your game's project. `samples/Sample.Stride.D3D12` does this.

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <StrideGraphicsApi>Direct3D12</StrideGraphicsApi>
</PropertyGroup>
```

## Initialize and render

The same code as the D3D11 quick start, with the `SkiaStrideD3D12*` types:

```cs
using SkiaGameRendering.Stride.D3D12;
using SkiaSharp;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;

using var game = new Game();
SkiaStrideD3D12RenderTarget2D? canvas = null;
var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };

// Called as a static method because Game.Run(GameContext) hides the toolkit's Run extension.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: rootScene =>
{
    game.SetupBase3DScene();
    var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;
    canvas = new SkiaStrideD3D12RenderTarget2D(game.GraphicsDevice, backBuffer.Width, backBuffer.Height);

    var renderer = new SkiaStrideD3D12SceneRenderer { Canvas = canvas };
    renderer.SkiaDraw += skCanvas =>
    {
        skCanvas.Clear(SKColors.Transparent);
        skCanvas.DrawCircle(100, 100, 100, paint);
    };
    game.AddSceneRenderer(renderer); // draws on top of the 3D scene every frame
});

canvas?.Dispose();
SkiaStrideD3D12Renderer.Dispose();
paint.Dispose();
```

## Things to know

- Skia submits its work to the GPU as soon as `End` runs, ahead of whatever Stride's current command
  list recorded earlier in the frame. Draw each target once per frame, before anything samples it.
- `EndWithoutDrawing` leaves the texture in Stride's `Common` layout. Before sampling it yourself,
  call `commandList.ResourceBarrierTransition(canvas.Texture, BarrierLayout.ShaderResource)`, as for
  any Stride D3D12 render target. `End` does this for its own composite.
- Supported color types: `Rgba8888` (the default), `Bgra8888`, `Rgba1010102` and `Rgba16161616`.
