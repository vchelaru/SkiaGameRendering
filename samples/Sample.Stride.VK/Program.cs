using SkiaGameRendering.Stride.VK;
using SkiaSharp;
using Stride.CommunityToolkit.Bepu;
using Stride.CommunityToolkit.Engine;
using Stride.Engine;
using Stride.Games;

// Stride's default authoring flow is GameStudio plus an asset-pipeline project; the Community
// Toolkit's code-only helpers (SetupBase3DScene, AddSceneRenderer) are its documented alternative
// for a pure-code Game like this one - see Sample.Stride.VK.csproj's comment. This sample forces
// StrideGraphicsApi=Vulkan (also in the csproj) so it exercises the Vulkan Skia interop
// (SkiaGameRendering.Stride.VK) rather than the default Direct3D11 path samples/Sample.Stride.D3D11 uses.
using var game = new Game();

SkiaStrideVulkanRenderTarget2D? canvas = null;
var paint = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };
float angle = 0f;

// Not game.Run(start: Start, ...): Stride.Games.GameBase already declares an instance
// Run(GameContext) method, and C# hides same-named extension methods behind ANY instance method of
// that name, matching arguments or not - game.Run(...) would resolve to GameBase's Run and fail to
// bind the start/update parameters. Calling GameExtensions.Run directly sidesteps that lookup.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start, update: Update);

void Start(Scene rootScene)
{
    // Camera, lighting, and a ground plane - the minimum a Stride scene needs to render anything.
    game.SetupBase3DScene();

    var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;

    // SkiaStrideVulkanRenderTarget2D auto-initializes SkiaStrideVulkanRenderer against
    // game.GraphicsDevice on first use. Sized to the back buffer so
    // SkiaStrideVulkanSceneRenderer's composite covers the screen.
    canvas = new SkiaStrideVulkanRenderTarget2D(game.GraphicsDevice, backBuffer.Width, backBuffer.Height);

    // SkiaStrideVulkanSceneRenderer is the DrawCore hook Stride needs to run a Skia draw every
    // frame - Stride drives rendering through its GraphicsCompositor, not a user-owned Draw() call
    // the way MonoGame/KNI do. game.AddSceneRenderer appends it after the existing scene renderer,
    // so it draws on top of the 3D scene SetupBase3DScene created.
    var renderer = new SkiaStrideVulkanSceneRenderer { Canvas = canvas };
    renderer.SkiaDraw += skCanvas =>
    {
        skCanvas.Clear(SKColors.Transparent);
        skCanvas.Save();
        skCanvas.RotateDegrees(angle, backBuffer.Width / 2f, backBuffer.Height / 2f);
        // Elongated on purpose - a rotating square looks like a diamond at 45 degrees, which reads
        // as a bug at a glance. An oval's silhouette stays unambiguous at every angle.
        skCanvas.DrawOval(
            SKRect.Create(backBuffer.Width / 2f - 140, backBuffer.Height / 2f - 70, 280, 140), paint);
        skCanvas.Restore();
    };
    game.AddSceneRenderer(renderer);
}

void Update(Scene rootScene, GameTime time)
{
    angle += (float)time.Elapsed.TotalSeconds * 90f;
}

// Not `using` declarations above: canvas must be disposed before SkiaStrideVulkanRenderer.Dispose()
// tears down the Vulkan interop it depends on, and both must happen after game.Run() returns (the
// game loop owns canvas/paint for its whole lifetime) but before `game`'s own `using` disposal runs.
canvas?.Dispose();
SkiaStrideVulkanRenderer.Dispose();
paint.Dispose();
