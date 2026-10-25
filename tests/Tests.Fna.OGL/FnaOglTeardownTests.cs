using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.Fna.OGL;

/// <summary>See <see cref="SkiaTeardownCheck"/>.</summary>
public sealed class FnaOglTeardownTests
{
    [Fact]
    public void DisposingSkia_LeavesEngineRenderTargetsWorking()
    {
        var errors = OneFrameGame.Render(() => new SkiaGlBackend(), SkiaTeardownCheck.EngineTargetsSurvive);
        Assert.True(errors.Length == 0, errors);
    }
}
