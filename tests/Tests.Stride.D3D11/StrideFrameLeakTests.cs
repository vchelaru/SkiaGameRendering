using SkiaGameRendering.Stride.D3D11;
using Stride.Graphics;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Stride;

/// <summary>
/// Draws and composites the scene for hundreds of frames on a WARP Stride device and fails if the
/// process grows per frame (see <see cref="StrideSkiaGolden.AssertNoPerFrameGrowth"/>).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class StrideFrameLeakTests(ITestOutputHelper output)
{
    [Fact]
    public void DrawingFrames_GrowsNothing()
    {
        Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", "1");
        try
        {
            using var graphicsDevice = GraphicsDevice.New();
            graphicsDevice.ColorSpace = ColorSpace.Gamma;
            try
            {
                StrideSkiaGolden.AssertNoPerFrameGrowth(graphicsDevice, output);
            }
            finally
            {
                SkiaStrideRenderer.Dispose();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", null);
        }
    }
}