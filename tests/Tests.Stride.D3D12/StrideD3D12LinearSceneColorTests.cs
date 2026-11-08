using SkiaGameRendering;
using Stride.Graphics;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.StrideD3D12;

/// <summary>
/// The samples' shared scene on a Linear color space device, where Stride's back buffer is sRGB-format:
/// every cell center must equal what the other runtimes draw. See <see cref="StrideSkiaGolden"/>.
/// </summary>
public sealed class StrideD3D12LinearSceneColorTests(ITestOutputHelper output)
{
    [Fact]
    public void SharedScene_OnLinearColorSpaceBackBuffer_MatchesOtherRuntimes()
    {
        using var device = WarpStrideDevice.Create(ColorSpace.Linear);
        try
        {
            StrideSkiaGolden.AssertSharedSceneMatchesOtherRuntimes(device, output);
        }
        finally
        {
            SkiaRenderer.Dispose();
        }
    }
}
