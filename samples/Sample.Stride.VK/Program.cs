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
// --screenshot <path> saves the engine back buffer after the composite (the final frame) to a PNG and exits;
// <path> with "-skia" before the extension gets the Skia texture alone.
var shotAt = Array.IndexOf(Environment.GetCommandLineArgs(), "--screenshot");
var shotPath = shotAt >= 0 && shotAt + 1 < Environment.GetCommandLineArgs().Length ? Environment.GetCommandLineArgs()[shotAt + 1] : null;
var exitCode = 0;
using var game = new Game();
// The Scene is a fixed 800x800 canvas; the window is sized to show it 1:1.
game.GraphicsDeviceManager.PreferredBackBufferWidth = SharedScene.Width;
game.GraphicsDeviceManager.PreferredBackBufferHeight = SharedScene.Height;

SkiaRenderTarget2D? canvas = null;
SkiaDirectCanvas? directCanvas = null;

// Not game.Run(start: Start, ...): Stride.Games.GameBase already declares an instance
// Run(GameContext) method, and C# hides same-named extension methods behind ANY instance method of
// that name, matching arguments or not - game.Run(...) would resolve to GameBase's Run and fail to
// bind the start/update parameters. Calling GameExtensions.Run directly sidesteps that lookup.
Stride.CommunityToolkit.Engine.GameExtensions.Run(game, start: Start);

void Start(Scene rootScene)
{
    // The Toolkit's Run overrides the preferred back buffer size above, so size the window here.
    game.Window.SetSize(new Stride.Core.Mathematics.Int2(SharedScene.Width, SharedScene.Height));
    var backBuffer = game.GraphicsDevice.Presenter.BackBuffer;

    // SkiaRenderTarget2D auto-initializes SkiaRenderer against
    // game.GraphicsDevice on first use. Sized to the back buffer so
    // SkiaSceneRenderer's composite covers the screen.
    var renderer = new SkiaSceneRenderer();
    if (direct)
        renderer.DirectCanvas = directCanvas = new SkiaDirectCanvas(game.GraphicsDevice);
    else
        renderer.Canvas = canvas = new SkiaRenderTarget2D(game.GraphicsDevice, SharedScene.Width, SharedScene.Height);

    // SkiaSceneRenderer is the DrawCore hook Stride needs to run a Skia draw every
    // frame - Stride drives rendering through its GraphicsCompositor, not a user-owned Draw() call
    // the way MonoGame/KNI do. game.AddSceneRenderer appends it after the existing scene renderer,
    // so it draws on top of the 3D scene Run's default SceneSetup3D created.
    renderer.SkiaDraw += skCanvas =>
    {
        // The 800x800 canvas covers the window, so this clear hides the 3D scene behind it (see Scene.Background).
        skCanvas.Clear(SharedScene.Background);
        SharedScene.Draw(skCanvas);
    };
    game.AddSceneRenderer(renderer);

    if (smokeTest || directSmokeTest)
        game.AddSceneRenderer(new SmokeCheckRenderer { Game = game, SetExitCode = code => exitCode = code });
    if (shotPath != null)
        game.AddSceneRenderer(new ScreenshotRenderer { Game = game, Path = shotPath, SkiaTexture = canvas?.Texture });
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

        // The 800x800 canvas fills the 800x800 back buffer: circle, drop, gradient and logo cells (cell 400),
        // plus an opaque gray pixel of the logo's text. The 3D scene shows through the margin, so it is not checked.
        var circle = At(SharedScene.CellCenter, SharedScene.CellCenter);
        var drop = At(SharedScene.Cell + SharedScene.CellCenter, SharedScene.CellCenter);
        var gradient = At(SharedScene.CellCenter, SharedScene.Cell + SharedScene.CellCenter);
        var logo = At(SharedScene.LogoOpaqueX, SharedScene.LogoOpaqueY);
        var clipped = At(SharedScene.ClipCenter, SharedScene.ClipCenter);
        var passed = circle.R > 200 && circle.G < 50 && circle.B < 50
            && clipped.R > 200 && clipped.G > 120 && clipped.B < 80
            && drop.R < 100 && drop.B > 150
            && gradient.R > 120 && gradient.G < 80 && gradient.B > 100
            && logo.R > 70 && logo.R < 150 && logo.G > 70 && logo.G < 150 && logo.B > 70 && logo.B < 150;

        Console.WriteLine($"Smoke test {(passed ? "passed" : "FAILED")}: format={backBuffer.ViewFormat}, circle={circle}, drop={drop}, gradient={gradient}, logo={logo}, clipped={clipped}");
        SetExitCode!(passed ? 0 : 1);
        Game.Exit();
    }
}

/// <summary>Saves the back buffer after a few frames (the composite is already done by then), then exits.</summary>
public sealed class ScreenshotRenderer : SceneRendererBase
{
    public Game? Game { get; set; }
    public string Path { get; set; } = "";
    public Texture? SkiaTexture { get; set; }
    int _frames;

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        if (++_frames < 10)
            return;
        Save(Game!.GraphicsDevice.Presenter.BackBuffer, drawContext, Path);
        if (SkiaTexture != null)
            Save(SkiaTexture, drawContext, System.IO.Path.ChangeExtension(Path, null) + "-skia.png");
        Game.Exit();
    }

    static void Save(Texture texture, RenderDrawContext drawContext, string path)
    {
        var pixels = texture.GetData<Color>(drawContext.CommandList);
        var bgra = texture.Format is PixelFormat.B8G8R8A8_UNorm or PixelFormat.B8G8R8A8_UNorm_SRgb;
        using var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(texture.Width, texture.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul));
        var rgba = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            rgba[i * 4] = bgra ? c.B : c.R;
            rgba[i * 4 + 1] = c.G;
            rgba[i * 4 + 2] = bgra ? c.R : c.B;
            rgba[i * 4 + 3] = c.A;
        }
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var png = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        System.IO.File.WriteAllBytes(path, png.ToArray());
        Console.WriteLine($"Saved {path} ({texture.Format})");
    }
}
