using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;

namespace Tests.Shared;

/// <summary>
/// Checks <see cref="SkiaRenderer.DrawToCurrentTarget"/> against the engine's own draws: a SpriteBatch rect
/// before, a Skia rect over it, then a SpriteBatch rect over the Skia one, read back through the engine.
/// Each probe pixel is covered by a known set of layers, so a vertical flip, a channel swap or a Skia draw
/// landing out of order each fails a specific probe. Linked into both DesktopGL test projects.
/// </summary>
static class DirectDrawLayering
{
    // OneFrameGame's back buffer size, so both cases probe the same pixels.
    internal const int Size = GoldenScene.Width;

    static readonly Color Background = new(20, 30, 200);
    static readonly Color Before = new(30, 180, 40);
    static readonly SKColor Skia = new(210, 50, 90);
    static readonly Color After = new(240, 220, 10);

    /// <summary>Draws the layers into whatever is bound and reports every probe that is wrong, or "".</summary>
    internal static string DrawAndCheck(GraphicsDevice graphicsDevice, Func<Color[]> readBack)
    {
        using var white = new Texture2D(graphicsDevice, 1, 1);
        white.SetData(new[] { Color.White });
        using var spriteBatch = new SpriteBatch(graphicsDevice);

        graphicsDevice.Clear(Background);

        // Before: the top-left quadrant.
        spriteBatch.Begin();
        spriteBatch.Draw(white, new Rectangle(0, 0, Size / 2, Size / 2), Before);
        spriteBatch.End();

        // Skia: a strip along the top, across the middle half. Drawn twice so the second call reuses the
        // cached surface.
        for (var i = 0; i < 2; i++)
        {
            SkiaRenderer.DrawToCurrentTarget(graphicsDevice, canvas =>
            {
                using var paint = new SKPaint { Color = Skia };
                canvas.DrawRect(Size / 4, 0, Size / 2, Size / 4, paint);
            });
        }

        // After: the right half, from 1/8 down to 1/2.
        spriteBatch.Begin();
        spriteBatch.Draw(white, new Rectangle(Size / 2, Size / 8, Size / 2, Size * 3 / 8), After);
        spriteBatch.End();

        var pixels = readBack();
        var skia = new Color(Skia.Red, Skia.Green, Skia.Blue, Skia.Alpha);
        var errors = new List<string>();
        void Probe(string what, int x, int y, Color expected)
        {
            var actual = pixels[y * Size + x];
            if (actual != expected)
                errors.Add($"{what} at ({x}, {y}): expected {expected}, got {actual}");
        }

        Probe("before only", Size / 8, Size / 8, Before);
        Probe("Skia over before", Size * 3 / 8, Size / 16, skia);
        Probe("Skia alone, at the top", Size * 5 / 8, Size / 16, skia);
        Probe("after over Skia", Size * 5 / 8, Size * 3 / 16, After);
        Probe("background where a flipped Skia rect would be", Size * 5 / 8, Size * 15 / 16, Background);
        Probe("background", Size / 8, Size * 7 / 8, Background);
        return string.Join("; ", errors);
    }

    internal static string BackBuffer(GraphicsDevice graphicsDevice)
    {
        graphicsDevice.SetRenderTarget(null);
        var parameters = graphicsDevice.PresentationParameters;
        if (parameters.BackBufferWidth != Size || parameters.BackBufferHeight != Size)
            return $"back buffer is {parameters.BackBufferWidth}x{parameters.BackBufferHeight}, expected {Size}x{Size}";

        return DrawAndCheck(graphicsDevice, () =>
        {
            var pixels = new Color[Size * Size];
            graphicsDevice.GetBackBufferData(pixels);
            return pixels;
        });
    }

    internal static string RenderTarget(GraphicsDevice graphicsDevice, int multiSampleCount)
    {
        using var renderTarget = new RenderTarget2D(graphicsDevice, Size, Size, false, SurfaceFormat.Color,
            DepthFormat.None, multiSampleCount, RenderTargetUsage.DiscardContents);
        // The engine clamps the count to what the driver supports; a silent 0 would skip the renderbuffer path.
        if (renderTarget.MultiSampleCount != multiSampleCount)
            return $"the engine made a {renderTarget.MultiSampleCount}x target, not {multiSampleCount}x";

        graphicsDevice.SetRenderTarget(renderTarget);
        try
        {
            return DrawAndCheck(graphicsDevice, () =>
            {
                // Unbinding is what resolves a multisampled target into its texture.
                graphicsDevice.SetRenderTarget(null);
                var pixels = new Color[Size * Size];
                renderTarget.GetData(pixels);
                return pixels;
            });
        }
        finally
        {
            graphicsDevice.SetRenderTarget(null);
        }
    }
}
