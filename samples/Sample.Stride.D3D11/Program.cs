using SkiaGameRendering.Stride.D3D11;
using SkiaSharp;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using Stride.Games;
// Aliased: Stride.Engine.Scene (a scene graph node) and Sample.Shared.Scene (the Skia drawing
// helper every sample shares) collide by name - they are unrelated types from unrelated domains.
using SharedScene = Sample.Shared.Scene;

// Stride's default authoring flow is GameStudio plus an asset-pipeline project; the Community
// Toolkit's code-only helpers (SetupBase3DScene, AddSceneRenderer) are its documented alternative
// for a pure-code Game like this one - see Sample.Stride.D3D11.csproj's comment.
using var game = new Game();

SkiaStrideRenderTarget2D? canvas = null;

// Not game.Run(start: Start, ...): Stride.Games.GameBase already declares an instance
// Run(GameContext) method, and C# hides same-named extension methods behind ANY instance method of
// that name, matching arguments or not - game.Run(...) would resolve to GameBase's Run and fail to
// bind the start/update parameters. Calling GameExtensions.Run directly sidesteps that lookup.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start);

void Start(Scene rootScene)
{
    // Camera, lighting, and a ground plane - the minimum a Stride scene needs to render anything.
    game.SetupBase3DScene();

    var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;

    // SkiaStrideRenderTarget2D auto-initializes SkiaStrideRenderer against game.GraphicsDevice on
    // first use. Sized to the back buffer so SkiaStrideSceneRenderer's composite covers the screen.
    canvas = new SkiaStrideRenderTarget2D(game.GraphicsDevice, backBuffer.Width, backBuffer.Height);

    // SkiaStrideSceneRenderer is the DrawCore hook Stride needs to run a Skia draw every frame -
    // Stride drives rendering through its GraphicsCompositor, not a user-owned Draw() call the way
    // MonoGame/KNI do. game.AddSceneRenderer appends it after the existing scene renderer, so it
    // draws on top of the 3D scene SetupBase3DScene created.
    var renderer = new SkiaStrideSceneRenderer { Canvas = canvas };
    renderer.SkiaDraw += skCanvas =>
    {
        skCanvas.Clear(SKColors.Transparent);
        SharedScene.Draw(skCanvas, backBuffer.Width, backBuffer.Height);
    };
    game.AddSceneRenderer(renderer);
}

// Not a `using` declaration above: canvas must be disposed before SkiaStrideRenderer.Dispose() tears
// down the ANGLE context it depends on, and both must happen after game.Run() returns (the game
// loop owns canvas for its whole lifetime) but before `game`'s own `using` disposal runs.
canvas?.Dispose();
SkiaStrideRenderer.Dispose();
