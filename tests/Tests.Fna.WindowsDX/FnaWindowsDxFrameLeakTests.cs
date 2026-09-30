using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Fna.WindowsDX;

/// <summary>
/// Draws and composites the scene hundreds of times inside one FNA D3D11 (WARP) game frame (see
/// <see cref="OneFrameGame"/> for why the device only exists inside a running game) and fails if the
/// process grows per pass (see <see cref="EngineFrameLeak"/>).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class FnaWindowsDxFrameLeakTests(ITestOutputHelper output)
{
    static FnaWindowsDxFrameLeakTests()
    {
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "D3D11");
        Environment.SetEnvironmentVariable("FNA3D_D3D11_USE_WARP", "1");
    }

    [Fact]
    public void DrawingFrames_GrowsNothing() =>
        OneFrameGame.Render(() => new SkiaFnaAngleBackend(), device =>
        {
            EngineFrameLeak.AssertNoPerFrameGrowth(device, output);
            return string.Empty;
        });
}