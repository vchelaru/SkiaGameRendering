using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Xunit.Abstractions;

namespace Tests.Shared;

/// <summary>
/// Runs a real <see cref="Game"/> for exactly one frame and hands back whatever the caller read off
/// the <see cref="GraphicsDevice"/> during it. Linked into both DesktopGL test projects - MonoGame
/// and KNI expose the same type names here, so one file covers both.
/// <para>
/// The WindowsDX tests get their device from <see cref="HeadlessGraphicsDevice"/> with no
/// <see cref="Game"/> at all. That is not an option on OpenGL, where the GL context comes from the
/// SDL window that only a running <see cref="Game"/> creates. So this pays for a real game loop, and
/// gets the engine's own initialization path exercised along with it.
/// </para>
/// </summary>
sealed class OneFrameGame : Game
{
    readonly Func<SkiaBackend> _backendFactory;
    readonly Func<GraphicsDevice, object> _readback;
    object? _result;
    Exception? _failure;

    OneFrameGame(Func<SkiaBackend> backendFactory, Func<GraphicsDevice, object> readback)
    {
        _backendFactory = backendFactory;
        _readback = readback;

        // Registers itself with the Game, which is what actually creates the device - hence no field.
        new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = GoldenScene.Width,
            PreferredBackBufferHeight = GoldenScene.Height,
        };
    }

    /// <param name="backendFactory">The engine's own <see cref="SkiaBackend"/>, constructed once the device exists.</param>
    /// <param name="readback">Runs inside <see cref="Draw"/>, with the Skia backend initialized and the GL context current.</param>
    internal static TResult Render<TResult>(Func<SkiaBackend> backendFactory, Func<GraphicsDevice, TResult> readback)
        where TResult : class
    {
        VendoredOpenGl.PreloadIfPresent();

        using var game = new OneFrameGame(backendFactory, device => readback(device));
        game.RunOneFrame();

        // Draw swallows its own exception rather than letting it escape through the game loop, where
        // the engine's shutdown path would run on top of it and bury the original stack.
        if (game._failure != null)
            throw new InvalidOperationException("The one-frame game failed to render.", game._failure);

        return (TResult?)game._result
            ?? throw new InvalidOperationException("The one-frame game never reached Draw.");
    }

    /// <summary>
    /// Renders one frame and compares the readback against a checked-in golden, but only where the
    /// rasterizer is pinned - a dev box runs this on its own GPU driver, which antialiases
    /// differently enough from CI's Mesa llvmpipe that comparing would fail for reasons that are not
    /// regressions. Same gate the <c>Core.OGL</c> and <c>Core.VK</c> goldens use, and for the same
    /// reason; the render still happens either way, and is still checked for orientation.
    /// </summary>
    /// <param name="callerFilePath">
    /// Filled in by the compiler and handed straight to <see cref="GoldenImage"/>, which uses it in
    /// update mode to find the source tree to write to. Without passing it along, an update would
    /// write into this shared file's own folder instead of the test project that asked for it.
    /// </param>
    internal static void RenderAndAssertGolden(Func<SkiaBackend> backendFactory,
        Func<GraphicsDevice, byte[]> readback, string goldenFileName, ITestOutputHelper output,
        [CallerFilePath] string callerFilePath = "")
    {
        var (adapter, rgba) = Render(backendFactory,
            device => Tuple.Create(device.Adapter.Description, readback(device)));

        output.WriteLine($"Rendered on '{adapter}'.");

        if (GoldenImage.PinnedRasterizerInUse)
        {
            GoldenImage.AssertMatchesGolden(rgba, goldenFileName, callerFilePath);
            return;
        }

        GoldenImage.AssertSceneOrientation(rgba);
        output.WriteLine("Golden comparison skipped: the golden was rendered by CI's pinned Mesa " +
            "llvmpipe build. The render itself still ran and was checked for orientation.");
    }

    protected override void Draw(GameTime gameTime)
    {
        try
        {
            // Everything Skia touches lives and dies inside this one frame: the backend's GRContext
            // belongs to the GL context the window owns, so it cannot outlive the game loop.
            SkiaRenderer.Initialize(_backendFactory(), GraphicsDevice);
            try
            {
                _result = _readback(GraphicsDevice);
            }
            finally
            {
                SkiaRenderer.Dispose();
            }
        }
        catch (Exception exception)
        {
            _failure = exception;
        }
        finally
        {
            Exit();
        }
    }
}
