using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Xunit.Abstractions;

namespace Tests.Shared;

/// <summary>
/// <see cref="FrameLeakCheck"/> over the frame a MonoGame, KNI or FNA game actually draws: bind an
/// engine render target, draw <see cref="GoldenScene"/> into a long-lived
/// <see cref="SkiaRenderTarget2D"/>, and let <see cref="SkiaRenderTarget2D.End"/> composite it through
/// the engine's <see cref="SpriteBatch"/>. Linked into each engine test project, like
/// <see cref="EngineSkiaGolden"/>.
/// </summary>
static class EngineFrameLeak
{
    internal static void AssertNoPerFrameGrowth(GraphicsDevice graphicsDevice, ITestOutputHelper output,
        (string Name, Func<long> Read)[]? counters = null)
    {
        using var renderTarget = new RenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height,
            false, SurfaceFormat.Color, DepthFormat.None);
        using var canvas = new SkiaRenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height);

        FrameLeakCheck.AssertNoPerFrameGrowth(() =>
        {
            graphicsDevice.SetRenderTarget(renderTarget);
            try
            {
                graphicsDevice.Clear(Color.Black);
                canvas.Begin();
                GoldenScene.Draw(canvas.Canvas);
                canvas.End();
            }
            finally
            {
                graphicsDevice.SetRenderTarget(null);
            }
        }, output, counters: counters);
    }
}
