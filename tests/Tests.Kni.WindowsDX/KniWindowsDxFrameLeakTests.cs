using SkiaGameRendering;
using SkiaGameRendering.Kni.WindowsDX;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Kni.WindowsDX;

/// <summary>
/// Draws and composites the scene for hundreds of frames on a headless WARP KNI device and fails if
/// the process grows per frame (see <see cref="EngineFrameLeak"/>).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class KniWindowsDxFrameLeakTests(ITestOutputHelper output)
{
    [Fact]
    public void DrawingFrames_GrowsNothing()
    {
        using var headless = new HeadlessGraphicsDevice(GoldenScene.Width, GoldenScene.Height);
        SkiaRenderer.Initialize(new SkiaKniAngleBackend(), headless.GraphicsDevice);
        try
        {
            EngineFrameLeak.AssertNoPerFrameGrowth(headless.GraphicsDevice, output);
        }
        finally
        {
            SkiaRenderer.Dispose();
        }
    }
}
