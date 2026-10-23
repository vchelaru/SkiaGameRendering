using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.Fna.OGL;

/// <summary>See <see cref="SkiaTeardownCheck"/>.</summary>
public sealed class FnaOglTeardownTests
{
    static FnaOglTeardownTests() =>
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "OpenGL");

    [Fact]
    public void DisposingSkia_LeavesEngineRenderTargetsWorking()
    {
        var errors = OneFrameGame.Render(() => new SkiaFnaGlBackend(), SkiaTeardownCheck.EngineTargetsSurvive);
        Assert.True(errors.Length == 0, errors);
    }
}
