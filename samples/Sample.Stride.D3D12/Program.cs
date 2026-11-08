using SkiaGameRendering;
using SkiaSharp;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using Stride.Games;
// Aliased: Stride.Engine.Scene (a scene graph node) and SampleShared.Scene (the Skia drawing
// helper every sample shares) collide by name.
using SharedScene = SampleShared.Scene;

// Same code-only setup as Sample.Stride.D3D11, on Stride's Direct3D 12 backend
// (StrideGraphicsApi=Direct3D12 in the csproj) through SkiaGameRendering.Stride.D3D12.
using var game = new Game();
// The Scene is a fixed 800x800 canvas; the window is sized to show it 1:1.
game.GraphicsDeviceManager.PreferredBackBufferWidth = SharedScene.Width;
game.GraphicsDeviceManager.PreferredBackBufferHeight = SharedScene.Height;

SkiaDirectCanvas? canvas = null;

// Not game.Run(start: Start): GameBase.Run(GameContext) hides the toolkit's Run extension method.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start);

void Start(Scene rootScene)
{
    // The Toolkit's Run overrides the preferred back buffer size above, so size the window here.
    game.Window.SetSize(new Int2(SharedScene.Width, SharedScene.Height));

    // Draws straight into the back buffer the compositor is rendering to. Constructing it initializes
    // SkiaRenderer against game.GraphicsDevice.
    canvas = new SkiaDirectCanvas(game.GraphicsDevice);

    // Appended after the existing scene renderer, so the Skia layer draws on top of the 3D scene.
    var renderer = new SkiaSceneRenderer { DirectCanvas = canvas };
    renderer.SkiaDraw += skCanvas =>
    {
        var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;
        SharedScene.Draw(skCanvas);
    };
    game.AddSceneRenderer(renderer);
}

// Canvas before renderer: the canvas needs the D3D12 interop the renderer owns. Both after the game
// loop ends and before `game` itself is disposed.
canvas?.Dispose();
SkiaRenderer.Dispose();
