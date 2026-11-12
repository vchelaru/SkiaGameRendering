using SkiaGameRendering;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Brutal;

/// <summary><see cref="SkiaRenderTarget2D"/>'s <c>multiSampleCount</c>: Skia draws 4x and resolves into the single-sample image.</summary>
public sealed class BrutalMultiSampleTests
{
    readonly ITestOutputHelper _output;

    public BrutalMultiSampleTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FourSamples_ResolveAntialiasedEdgesIntoTheImage(bool appManagesLayout)
    {
        const int Size = 64;
        using var vk = new BrutalDevice();
        using var target = new SkiaRenderTarget2D(vk.Context, Size, Size, appManagesLayout: appManagesLayout, multiSampleCount: 4);

        // Two frames, the second drawing over the first, so the resolve and the layout hand-back are both exercised again.
        target.Begin();
        MultiSampleChecks.DrawTriangle(target.Canvas);
        target.End();
        target.Begin(clear: false);
        using (var paint = new SKPaint { Color = SKColors.Blue })
            target.Canvas.DrawRect(0, 0, 4, 4, paint);
        target.End(synchronous: true);

        var rgba = vk.ReadImage(target, appManagesLayout);
        MultiSampleChecks.AssertFourSampleTriangle(rgba, Size, _output);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, rgba[..4]);
    }

    [Fact]
    public void InvalidCountsThrow()
    {
        using var vk = new BrutalDevice();
        foreach (var count in new[] { -1, 1, 3, 16 })
            Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(vk.Context, 16, 16, multiSampleCount: count));
    }
}
