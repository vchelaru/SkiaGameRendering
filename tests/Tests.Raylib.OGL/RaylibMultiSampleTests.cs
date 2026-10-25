using SkiaGameRendering;
using Xunit;

namespace Tests.Raylib.OGL;

/// <summary>
/// raylib has no multisampled <see cref="SkiaRenderTarget2D"/>, so a non-zero <c>multiSampleCount</c> must throw
/// rather than fall back to a plain surface. The constructor refuses before it needs a window.
/// </summary>
public sealed class RaylibMultiSampleTests
{
    [NotMacOsFact]
    public void FlagIsFalse() => Assert.False(SkiaRenderer.SupportsMultiSampledTargets);

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(16)]
    public void NonZeroMultiSampleCount_Throws(int count) =>
        Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(16, 16, multiSampleCount: count));
}
