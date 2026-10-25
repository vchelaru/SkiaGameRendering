using SkiaGameRendering;
using SkiaSharp;
using Xunit;

namespace Tests.WindowsDX12;

/// <summary>
/// SkiaDx12Backend cannot multisample a render target, so a non-zero <c>multiSampleCount</c> must throw
/// <see cref="NotSupportedException"/> rather than fall back to a plain surface. The check runs before
/// any device or texture is touched, so it needs no GPU; the end-to-end throw through a live
/// <c>GraphicsDevice</c> is not covered here (no headless MonoGame native device in the test projects).
/// </summary>
public sealed class MonoGameWindowsDx12MultiSampleTests
{
    [Fact]
    public void DoesNotAdvertiseMultiSampledTargets() =>
        Assert.False(new SkiaDx12Backend().SupportsMultiSampledTargets);

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(1)]
    [InlineData(16)]
    public void NonZeroMultiSampleCount_Throws(int multiSampleCount) =>
        Assert.Throws<NotSupportedException>(() =>
            new SkiaDx12Backend().CreateTarget(32, 32, SKColorType.Rgba8888, multiSampleCount));
}
