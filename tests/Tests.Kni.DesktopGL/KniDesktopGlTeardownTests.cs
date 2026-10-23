using SkiaGameRendering.Kni.DesktopGL;
using Tests.Shared;
using Xunit;

namespace Tests.Kni.DesktopGL;

/// <summary>See <see cref="SkiaTeardownCheck"/>.</summary>
public sealed class KniDesktopGlTeardownTests
{
    [Fact]
    public void DisposingSkia_LeavesEngineRenderTargetsWorking()
    {
        var errors = OneFrameGame.Render(() => new SkiaKniGlBackend(), SkiaTeardownCheck.EngineTargetsSurvive);
        Assert.True(errors.Length == 0, errors);
    }
}
