using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Fna.OGL;

/// <summary>
/// Draws and composites the scene hundreds of times inside one FNA OpenGL game frame (see
/// <see cref="OneFrameGame"/> for why the device only exists inside a running game) and fails if the
/// process grows per pass (see <see cref="EngineFrameLeak"/>).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class FnaOglFrameLeakTests(ITestOutputHelper output)
{
    static FnaOglFrameLeakTests() =>
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "OpenGL");

    [Fact]
    public void DrawingFrames_GrowsNothing() =>
        OneFrameGame.Render(() => new SkiaFnaGlBackend(), device =>
        {
            EngineFrameLeak.AssertNoPerFrameGrowth(device, output);
            return string.Empty;
        });
}