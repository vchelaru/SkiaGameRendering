using SkiaGameRendering;
using Stride.Graphics;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Stride;

/// <summary>
/// The samples' shared scene on a Linear color space device, where Stride's back buffer is sRGB-format:
/// every cell center must equal what the other runtimes draw. See <see cref="StrideSkiaGolden"/>.
/// </summary>
public sealed class StrideLinearSceneColorTests(ITestOutputHelper output)
{
    [Fact]
    public void SharedScene_OnLinearColorSpaceBackBuffer_MatchesOtherRuntimes()
    {
        Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", "1");
        try
        {
            using var device = GraphicsDevice.New();
            device.ColorSpace = ColorSpace.Linear;
            try
            {
                StrideSkiaGolden.AssertSharedSceneMatchesOtherRuntimes(device, output);
            }
            finally
            {
                SkiaRenderer.Dispose();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", null);
        }
    }
}
