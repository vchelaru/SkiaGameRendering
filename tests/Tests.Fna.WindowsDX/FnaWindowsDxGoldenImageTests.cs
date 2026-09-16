using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.Fna.WindowsDX;

/// <summary>
/// Renders <see cref="GoldenScene"/> through <c>SkiaRenderTarget2D</c> on a real FNA
/// <see cref="Microsoft.Xna.Framework.Graphics.GraphicsDevice"/> and compares the pixels against a
/// checked-in reference. Same two assertions as <c>Tests.WindowsDX</c>'s MonoGame golden, for the
/// same reasons.
/// <para>
/// FNA has no headless device: <c>FNA3D_PrepareWindowAttributes</c> has to run before the device
/// exists and only FNA's own window creation calls it, so this pays for a one-frame <c>Game</c>
/// (<see cref="OneFrameGame"/>) the way the DesktopGL tests do. FNA creates the window hidden and
/// <c>RunOneFrame</c> never shows it, so nothing flashes on screen.
/// </para>
/// <para>
/// Two FNA3D hints, read from the environment by SDL, make this deterministic anywhere:
/// <c>FNA3D_FORCE_DRIVER=D3D11</c>, without which FNA3D picks SDL_GPU and the backend has no
/// device to share (the same thing every FNA game using this backend has to set), and
/// <c>FNA3D_D3D11_USE_WARP=1</c>, which puts the device on WARP - the same rasterizer
/// <c>Tests.Core.ANGLE</c> and the MonoGame/KNI WindowsDX goldens run on, so this compares
/// everywhere with no pinned-rasterizer gate.
/// </para>
/// </summary>
public sealed class FnaWindowsDxGoldenImageTests
{
    const string Golden = "fna-windowsdx-scene.png";

    static FnaWindowsDxGoldenImageTests()
    {
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "D3D11");
        Environment.SetEnvironmentVariable("FNA3D_D3D11_USE_WARP", "1");
    }

    [Fact]
    public void Scene_DrawnThroughSkiaRenderTarget2D_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            OneFrameGame.Render(() => new SkiaFnaAngleBackend(), EngineSkiaGolden.RenderSceneToSkiaTexture),
            Golden);

    [Fact]
    public void Scene_CompositedOntoEngineRenderTarget_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            OneFrameGame.Render(() => new SkiaFnaAngleBackend(), EngineSkiaGolden.RenderSceneCompositedByEngine),
            Golden);
}
