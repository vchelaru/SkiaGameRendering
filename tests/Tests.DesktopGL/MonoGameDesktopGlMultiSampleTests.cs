using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.DesktopGL;

/// <summary>
/// <c>SkiaRenderTarget2D</c>'s <c>multiSampleCount</c> on the GL backend, which supports it. The default
/// (0) is covered by the golden tests, which stay unchanged.
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class MonoGameDesktopGlMultiSampleTests(ITestOutputHelper output)
{
    [Fact]
    public void FourSamples_MatchesPlainAwayFromEdges() =>
        OneFrameGame.Render(() => new SkiaGlBackend(), device =>
        {
            EngineMultiSample.AssertMultiSampledMatchesPlainAwayFromEdges(device, output);
            return string.Empty;
        });

    [Fact]
    public void UnsupportedCounts_Throw() =>
        OneFrameGame.Render(() => new SkiaGlBackend(), device =>
        {
            EngineMultiSample.AssertInvalidCountsThrow(device);
            return string.Empty;
        });

    [Fact]
    public void DisposingMultiSampledTargets_GrowsNothing() =>
        OneFrameGame.Render(() => new SkiaGlBackend(), device =>
        {
            EngineMultiSample.AssertNoGrowthOverMultiSampledFrames(device, output);
            return string.Empty;
        });
}
