using SkiaGameRendering;
using SkiaSharp;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
// Aliased: Stride.Engine.Scene (a scene graph node) and SampleShared.Scene (the Skia drawing
// helper every sample shares) collide by name - they are unrelated types from unrelated domains.
using SharedScene = SampleShared.Scene;

// Stride's default authoring flow is GameStudio plus an asset-pipeline project; the Community
// Toolkit's code-only helpers (the default SceneSetup3D that Run applies, AddSceneRenderer) are its documented alternative
// for a pure-code Game like this one - see Sample.Stride.VK.csproj's comment. This sample forces
// StrideGraphicsApi=Vulkan (also in the csproj) so it exercises the Vulkan Skia interop
// (SkiaGameRendering.Stride.VK) rather than the default Direct3D11 path samples/Sample.Stride.D3D11 uses.
//
// --smoke-test draws through SkiaRenderTarget2D, --direct-smoke-test through SkiaSceneRenderer.DirectCanvas.
// Both read the back buffer after a few frames, check the shared scene's first two cells, and exit
// with 0 or 1.
var smokeTest = args.Contains("--smoke-test");
var directSmokeTest = args.Contains("--direct-smoke-test");
var direct = directSmokeTest;
var exitCode = 0;
using var game = new Game();

SkiaRenderTarget2D? canvas = null;
SkiaDirectCanvas? directCanvas = null;

// Not game.Run(start: Start, ...): Stride.Games.GameBase already declares an instance
// Run(GameContext) method, and C# hides same-named extension methods behind ANY instance method of
// that name, matching arguments or not - game.Run(...) would resolve to GameBase's Run and fail to
// bind the start/update parameters. Calling GameExtensions.Run directly sidesteps that lookup.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start);

void Start(Scene rootScene)
{
    var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;

    // SkiaRenderTarget2D auto-initializes SkiaRenderer against
    // game.GraphicsDevice on first use. Sized to the back buffer so
    // SkiaSceneRenderer's composite covers the screen.
    var renderer = new SkiaSceneRenderer();
    if (direct)
        renderer.DirectCanvas = directCanvas = new SkiaDirectCanvas(game.GraphicsDevice);
    else
        renderer.Canvas = canvas = new SkiaRenderTarget2D(game.GraphicsDevice, backBuffer.Width, backBuffer.Height);

    // SkiaSceneRenderer is the DrawCore hook Stride needs to run a Skia draw every
    // frame - Stride drives rendering through its GraphicsCompositor, not a user-owned Draw() call
    // the way MonoGame/KNI do. game.AddSceneRenderer appends it after the existing scene renderer,
    // so it draws on top of the 3D scene Run's default SceneSetup3D created.
    renderer.SkiaDraw += skCanvas =>
    {
        // A direct canvas draws on top of the 3D scene, so clearing would wipe it.
        if (!direct)
            skCanvas.Clear(SKColors.Transparent);
        SharedScene.Draw(skCanvas, backBuffer.Width, backBuffer.Height);
    };
    game.AddSceneRenderer(renderer);

    if (smokeTest || directSmokeTest)
        game.AddSceneRenderer(new SmokeCheckRenderer { Game = game, SetExitCode = code => exitCode = code });
}

// Not a `using` declaration above: canvas must be disposed before SkiaRenderer.Dispose()
// tears down the Vulkan interop it depends on, and both must happen after game.Run() returns (the
// game loop owns canvas for its whole lifetime) but before `game`'s own `using` disposal runs.
canvas?.Dispose();
directCanvas?.Dispose();
SkiaRenderer.Dispose();
return exitCode;

/// <summary>
/// Runs after <see cref="SkiaSceneRenderer"/> in the compositor. After a few frames it reads the back
/// buffer and checks the center of the shared scene's first two cells (the red circle and the blue water
/// drop), then exits the game. Everything else on screen is the 3D scene, so it is not checked.
/// </summary>
public sealed class SmokeCheckRenderer : SceneRendererBase
{
    public Game? Game { get; set; }
    public Action<int>? SetExitCode { get; set; }

    const int WarmupFrames = 5;
    int _frames;

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        if (++_frames < WarmupFrames)
            return;

        var backBuffer = Game!.GraphicsDevice.Presenter.BackBuffer;
        var pixels = backBuffer.GetData<Color>(drawContext.CommandList);
        // GetData hands back the texture's bytes in Stride's RGBA Color, so a BGRA back buffer reads swapped.
        var bgra = backBuffer.Format is PixelFormat.B8G8R8A8_UNorm or PixelFormat.B8G8R8A8_UNorm_SRgb;
        Color At(float x, float y)
        {
            var c = pixels[(int)y * backBuffer.Width + (int)x];
            return bgra ? new Color(c.B, c.G, c.R, c.A) : c;
        }

        var cell = Math.Min(backBuffer.Width, backBuffer.Height) / 2f;
        var circle = At(cell / 2, cell / 2);
        var drop = At(cell * 1.5f, cell / 2);
        var passed = circle.R > 200 && circle.G < 50 && circle.B < 50
            && drop.R < 100 && drop.B > 150;

        Console.WriteLine($"Smoke test {(passed ? "passed" : "FAILED")}: format={backBuffer.ViewFormat}, circle={circle}, drop={drop}");
        SetExitCode!(passed ? 0 : 1);
        Game.Exit();
    }
}
