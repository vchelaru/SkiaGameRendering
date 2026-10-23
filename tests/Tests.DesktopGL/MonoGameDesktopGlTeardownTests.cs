using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.DesktopGL;

/// <summary>See <see cref="SkiaTeardownCheck"/>.</summary>
public sealed class MonoGameDesktopGlTeardownTests
{
    [Fact]
    public void DisposingSkia_LeavesEngineRenderTargetsWorking()
    {
        var errors = OneFrameGame.Render(() => new SkiaGlBackend(), SkiaTeardownCheck.EngineTargetsSurvive);
        Assert.True(errors.Length == 0, errors);
    }
}
