using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;

namespace Tests.Shared;

/// <summary>
/// A <see cref="SkiaRenderTarget2D"/> is reused every frame, so Skia rewrites the same shared texture on
/// its own GL context while the engine's context keeps sampling it. The engine context's view of a shared
/// texture only refreshes on a new bind, so a target that went from a small draw to a full-surface fill
/// kept showing most of the old contents when composited to the window (found in Gum: a full-screen
/// background shown in blocky patches). Only reproduces across real frames, with the composite going to
/// the back buffer. Linked into the engine GL test projects, one test each.
/// </summary>
sealed class ReusedTargetGame : Game
{
    const int Width = 256;
    const int Height = 256;
    const int SmallFrames = 20;
    static readonly Color Fill = new(0, 0, 66);

    readonly Func<SkiaBackend> _backendFactory;
    SkiaRenderTarget2D? _canvas;
    string? _result;
    Exception? _failure;
    int _frame;

    ReusedTargetGame(Func<SkiaBackend> backendFactory)
    {
        _backendFactory = backendFactory;
        new GraphicsDeviceManager(this) { PreferredBackBufferWidth = Width, PreferredBackBufferHeight = Height };
    }

    /// <summary>
    /// Draws a small item on a transparent surface for several frames, as a menu does, then an opaque fill
    /// over the whole surface with the same small item on top, and reports how many back-buffer pixels do
    /// not show the new frame ("0 of N ..." when the whole fill is composited).
    /// </summary>
    internal static string Run(Func<SkiaBackend> backendFactory) => OneFrameGame.GameThread.Run(() =>
    {
        VendoredOpenGl.PreloadIfPresent();
        using var game = new ReusedTargetGame(backendFactory);
        game.Run();
        if (game._failure != null)
            throw new InvalidOperationException("The game failed to render.", game._failure);
        return game._result ?? throw new InvalidOperationException("The game never finished.");
    });

    protected override void Draw(GameTime gameTime)
    {
        try
        {
            if (!SkiaRenderer.IsInitialized)
                SkiaRenderer.Initialize(_backendFactory(), GraphicsDevice);
            _canvas ??= new SkiaRenderTarget2D(GraphicsDevice, Width, Height);

            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.CornflowerBlue);
            _canvas.Begin();
            var surface = _canvas.Canvas;
            var isFillFrame = _frame == SmallFrames;
            if (isFillFrame)
                surface.Clear(new SKColor(Fill.R, Fill.G, Fill.B));
            using (var paint = new SKPaint { Color = SKColors.White })
                surface.DrawRect(10, 10, 40, 40, paint);
            _canvas.End();

            if (isFillFrame)
            {
                var pixels = new Color[Width * Height];
                GraphicsDevice.GetBackBufferData(pixels);
                var wrong = pixels.Count(pixel => pixel != Fill && pixel != Color.White);
                _result = $"{wrong} of {pixels.Length} pixels did not show the new frame";
                Exit();
            }

            _frame++;
        }
        catch (Exception exception)
        {
            _failure = exception;
            Exit();
        }

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _canvas?.Dispose();
        if (SkiaRenderer.IsInitialized)
            SkiaRenderer.Dispose();
        base.UnloadContent();
    }
}
