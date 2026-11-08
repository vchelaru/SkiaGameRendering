using SkiaGameRendering;
using SkiaSharp;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using Stride.Games;
// Aliased: Stride.Engine.Scene (a scene graph node) and SampleShared.Scene (the Skia drawing
// helper every sample shares) collide by name - they are unrelated types from unrelated domains.
using SharedScene = SampleShared.Scene;

// Stride's default authoring flow is GameStudio plus an asset-pipeline project; the Community
// Toolkit's code-only helpers (the default SceneSetup3D that Run applies, AddSceneRenderer) are its documented alternative
// for a pure-code Game like this one - see Sample.Stride.D3D11.csproj's comment.
using var game = new Game();
// The Scene is a fixed 800x800 canvas; the window is sized to show it 1:1.
game.GraphicsDeviceManager.PreferredBackBufferWidth = SharedScene.Width;
game.GraphicsDeviceManager.PreferredBackBufferHeight = SharedScene.Height;

SkiaDirectCanvas? canvas = null;

// Not game.Run(start: Start, ...): Stride.Games.GameBase already declares an instance
// Run(GameContext) method, and C# hides same-named extension methods behind ANY instance method of
// that name, matching arguments or not - game.Run(...) would resolve to GameBase's Run and fail to
// bind the start/update parameters. Calling GameExtensions.Run directly sidesteps that lookup.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start);

void Start(Scene rootScene)
{
    // The Toolkit's Run overrides the preferred back buffer size above, so size the window here.
    game.Window.SetSize(new Stride.Core.Mathematics.Int2(SharedScene.Width, SharedScene.Height));
    // Camera, lighting, and a ground plane - the minimum a Stride scene needs to render anything.

    // SkiaDirectCanvas auto-initializes SkiaRenderer against game.GraphicsDevice on first
    // use, and draws straight into the back buffer the compositor is rendering to.
    canvas = new SkiaDirectCanvas(game.GraphicsDevice);

    // SkiaSceneRenderer is the DrawCore hook Stride needs to run a Skia draw every frame -
    // Stride drives rendering through its GraphicsCompositor, not a user-owned Draw() call the way
    // MonoGame/KNI do. game.AddSceneRenderer appends it after the existing scene renderer, so it
    // draws on top of the 3D scene Run's default SceneSetup3D created.
    var renderer = new SkiaSceneRenderer { DirectCanvas = canvas };
    renderer.SkiaDraw += skCanvas =>
    {
        var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;
        // The 800x800 canvas covers the window, so this clear hides the 3D scene behind it (see Scene.Background).
        skCanvas.Clear(SharedScene.Background);
        SharedScene.Draw(skCanvas);
    };
    game.AddSceneRenderer(renderer);
}

// Not a `using` declaration above: both must happen after game.Run() returns (the game loop owns
// canvas for its whole lifetime) but before `game`'s own `using` disposal runs.
canvas?.Dispose();
SkiaRenderer.Dispose();
