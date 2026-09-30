using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.DesktopGL;

/// <summary>
/// Draws and composites the scene hundreds of times inside one MonoGame DesktopGL game frame (see
/// <see cref="OneFrameGame"/> for why the device only exists inside a running game) and fails if the
/// process grows per pass (see <see cref="EngineFrameLeak"/>).
/// </summary>
[Collection(FrameLeakCheck.Collection)]
public sealed class MonoGameDesktopGlFrameLeakTests(ITestOutputHelper output)
{
    [Fact]
    public void DrawingFrames_GrowsNothing() =>
        OneFrameGame.Render(() => new SkiaGlBackend(), device =>
        {
            EngineFrameLeak.AssertNoPerFrameGrowth(device, output);
            return string.Empty;
        });
}