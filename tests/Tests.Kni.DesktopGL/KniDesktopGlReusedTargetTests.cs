using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Kni.DesktopGL;

/// <summary>KNI skips <c>glBindTexture</c> for a texture it thinks is already bound; see <see cref="ReusedTargetGame"/>.</summary>
public sealed class KniDesktopGlReusedTargetTests(ITestOutputHelper output)
{
    [Fact]
    public void ReusedTarget_FullSurfaceFillAfterSmallDraws_IsFullyComposited()
    {
        var result = ReusedTargetGame.Run(() => new SkiaGlBackend());
        output.WriteLine(result);
        Assert.StartsWith("0 of ", result);
    }
}
