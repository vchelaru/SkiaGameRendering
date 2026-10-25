using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.Kni.WindowsDX;

/// <summary>
/// The ANGLE/D3D11 backend cannot multisample a render target, so a non-zero <c>multiSampleCount</c> must
/// throw rather than fall back to a plain surface.
/// </summary>
[Collection(SkiaRendererCollection.Name)]
public sealed class KniWindowsDxMultiSampleTests : IDisposable
{
    readonly HeadlessGraphicsDevice _headless = new(GoldenScene.Width, GoldenScene.Height);

    public KniWindowsDxMultiSampleTests() =>
        SkiaRenderer.Initialize(new SkiaAngleBackend(), _headless.GraphicsDevice);

    public void Dispose()
    {
        SkiaRenderer.Dispose();
        _headless.Dispose();
    }

    [Fact]
    public void FlagIsFalse() => Assert.False(SkiaRenderer.SupportsMultiSampledTargets);

    [Fact]
    public void MultiSampleCount_Throws() => EngineMultiSample.AssertMultiSampleThrows(_headless.GraphicsDevice);

    [Fact]
    public void UnsupportedCounts_Throw() => EngineMultiSample.AssertInvalidCountsThrow(_headless.GraphicsDevice);
}
