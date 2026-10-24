using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.Fna.WindowsDX;

/// <summary>The FNA ANGLE/D3D11 backend cannot multisample a render target, so a non-zero count must throw.</summary>
public sealed class FnaWindowsDxMultiSampleTests
{
    static FnaWindowsDxMultiSampleTests()
    {
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "D3D11");
        Environment.SetEnvironmentVariable("FNA3D_D3D11_USE_WARP", "1");
    }

    [Fact]
    public void MultiSampleCount_Throws() =>
        OneFrameGame.Render(() => new SkiaAngleBackend(), device =>
        {
            EngineMultiSample.AssertMultiSampleThrows(device);
            return string.Empty;
        });
}
