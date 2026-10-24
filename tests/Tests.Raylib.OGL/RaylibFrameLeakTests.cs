using Raylib_cs;
using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;
using RaylibApi = Raylib_cs.Raylib;

namespace Tests.Raylib.OGL;

/// <summary>
/// Runs raylib's own frame loop for hundreds of frames, drawing the scene through
/// <see cref="SkiaRenderTarget2D"/> and compositing it with <c>End</c>, and fails if the process
/// grows per frame (see <see cref="FrameLeakCheck"/>). Linux only, like
/// <see cref="RaylibGoldenImageTests"/>.
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class RaylibFrameLeakTests(ITestOutputHelper output)
{
    [LinuxOnlyFact]
    public void DrawingFrames_GrowsNothing()
    {
        RaylibApi.SetConfigFlags(ConfigFlags.HiddenWindow);
        RaylibApi.InitWindow(GoldenScene.Width, GoldenScene.Height, "Tests.Raylib.OGL");
        try
        {
            SkiaRenderer.Initialize();
            try
            {
                using var canvas = new SkiaRenderTarget2D(GoldenScene.Width, GoldenScene.Height);
                FrameLeakCheck.AssertNoPerFrameGrowth(() =>
                {
                    RaylibApi.BeginDrawing();
                    RaylibApi.ClearBackground(Color.Black);
                    canvas.Begin();
                    GoldenScene.Draw(canvas.Canvas);
                    canvas.End();
                    RaylibApi.EndDrawing();
                }, output);
            }
            finally
            {
                SkiaRenderer.Dispose();
            }
        }
        finally
        {
            RaylibApi.CloseWindow();
        }
    }
}
