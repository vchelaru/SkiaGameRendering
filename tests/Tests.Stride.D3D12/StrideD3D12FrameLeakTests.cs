using SkiaGameRendering.Stride.D3D12;
using Stride.Graphics;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.StrideD3D12;

/// <summary>
/// Draws and composites the scene for hundreds of frames on a WARP Stride device and fails if the
/// process grows per frame (see <see cref="StrideSkiaGolden.AssertNoPerFrameGrowth"/>).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class StrideD3D12FrameLeakTests(ITestOutputHelper output)
{
    [Fact]
    public void DrawingFrames_GrowsNothing()
    {
        using var graphicsDevice = WarpStrideDevice.Create(ColorSpace.Gamma);
        try
        {
            StrideSkiaGolden.AssertNoPerFrameGrowth(graphicsDevice, output);
        }
        finally
        {
            SkiaStrideD3D12Renderer.Dispose();
        }
    }
}