using SkiaGameRendering;
using Xunit;

namespace Tests.Godot;

/// <summary>
/// <see cref="SkiaRenderTarget2D"/>'s <c>multiSampleCount</c> on Godot: only 0 works. The constructor throws
/// before it touches the engine, so this needs no Godot binary.
/// </summary>
public class GodotMultiSampleTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public void UnsupportedCountThrowsNotSupported(int multiSampleCount) =>
        Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(16, 16, multiSampleCount: multiSampleCount));

    [Fact]
    public void SupportsMultiSampledTargetsIsFalse() => Assert.False(SkiaRenderer.SupportsMultiSampledTargets);
}
