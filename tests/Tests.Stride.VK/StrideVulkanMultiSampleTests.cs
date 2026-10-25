using System.Runtime.CompilerServices;
using SkiaGameRendering;
using Stride.Graphics;
using Xunit;

namespace Tests.StrideVK;

/// <summary>
/// Stride has no multisampled <see cref="SkiaRenderTarget2D"/>, so a non-zero <c>multiSampleCount</c> must throw
/// rather than fall back to a plain surface. The constructor refuses before it touches the device, so no GPU is needed.
/// </summary>
public sealed class StrideVulkanMultiSampleTests
{
    static GraphicsDevice Device => (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

    [Fact]
    public void FlagIsFalse() => Assert.False(SkiaRenderer.SupportsMultiSampledTargets);

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(16)]
    public void NonZeroMultiSampleCount_Throws(int count) =>
        Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(Device, 16, 16, multiSampleCount: count));
}
