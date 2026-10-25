using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Fna.OGL;

/// <summary>See <see cref="ReusedTargetGame"/>.</summary>
public sealed class FnaOglReusedTargetTests(ITestOutputHelper output)
{
    [Fact]
    public void ReusedTarget_FullSurfaceFillAfterSmallDraws_IsFullyComposited()
    {
        var result = ReusedTargetGame.Run(() => new SkiaGlBackend());
        output.WriteLine(result);
        Assert.StartsWith("0 of ", result);
    }
}
