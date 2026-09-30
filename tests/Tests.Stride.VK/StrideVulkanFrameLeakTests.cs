using SkiaGameRendering.Stride.VK;
using Stride.Graphics;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.StrideVK;

/// <summary>
/// Draws and composites the scene for hundreds of frames on a Stride Vulkan device (lavapipe in CI)
/// and fails if the process grows per frame (see <see cref="StrideSkiaGolden.AssertNoPerFrameGrowth"/>).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class StrideVulkanFrameLeakTests(ITestOutputHelper output)
{
    [Fact]
    public void DrawingFrames_GrowsNothing()
    {
        using var graphicsDevice = GraphicsDevice.New();
        graphicsDevice.ColorSpace = ColorSpace.Gamma;
        try
        {
            StrideSkiaGolden.AssertNoPerFrameGrowth(graphicsDevice, output);
        }
        finally
        {
            SkiaStrideVulkanRenderer.Dispose();
        }
    }
}