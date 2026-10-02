using SkiaGameRendering.Stride.D3D12;
using SkiaSharp;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using Stride.Games;
// Aliased: Stride.Engine.Scene (a scene graph node) and Sample.Shared.Scene (the Skia drawing
// helper every sample shares) collide by name.
using SharedScene = Sample.Shared.Scene;

// Same code-only setup as Sample.Stride.D3D11, on Stride's Direct3D 12 backend
// (StrideGraphicsApi=Direct3D12 in the csproj) through SkiaGameRendering.Stride.D3D12.
using var game = new Game();

SkiaStrideD3D12RenderTarget2D? canvas = null;

// Not game.Run(start: Start): GameBase.Run(GameContext) hides the toolkit's Run extension method.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start);

void Start(Scene rootScene)
{
    game.SetupBase3DScene();

    var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;

    // Sized to the back buffer so the composite covers the screen. Constructing it initializes
    // SkiaStrideD3D12Renderer against game.GraphicsDevice.
    canvas = new SkiaStrideD3D12RenderTarget2D(game.GraphicsDevice, backBuffer.Width, backBuffer.Height);

    // Appended after the existing scene renderer, so the Skia layer draws on top of the 3D scene.
    var renderer = new SkiaStrideD3D12SceneRenderer { Canvas = canvas };
    renderer.SkiaDraw += skCanvas =>
    {
        skCanvas.Clear(SKColors.Transparent);
        SharedScene.Draw(skCanvas, backBuffer.Width, backBuffer.Height);
    };
    game.AddSceneRenderer(renderer);
}

// Canvas before renderer: the canvas needs the D3D12 interop the renderer owns. Both after the game
// loop ends and before `game` itself is disposed.
canvas?.Dispose();
SkiaStrideD3D12Renderer.Dispose();
