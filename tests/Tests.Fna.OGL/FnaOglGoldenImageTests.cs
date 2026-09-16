using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Fna.OGL;

/// <summary>
/// Renders <see cref="GoldenScene"/> through <c>SkiaRenderTarget2D</c> on a real FNA
/// <see cref="Microsoft.Xna.Framework.Graphics.GraphicsDevice"/> on FNA3D's OpenGL driver, via a
/// hidden one-frame <c>Game</c> (see <c>Tests.Fna.WindowsDX</c> for why FNA needs a Game at all).
/// The OpenGL counterpart of that project's golden, with the same Mesa-gated comparison the
/// MonoGame/KNI DesktopGL goldens use: on CI the render is on llvmpipe and compares against the
/// checked-in image; on a dev box it runs on the real driver and is only checked for orientation.
/// <para>
/// <c>FNA3D_FORCE_DRIVER=OpenGL</c> is the same hint every FNA game using this backend has to set;
/// without it FNA3D picks SDL_GPU and the backend has no context to share.
/// </para>
/// </summary>
public sealed class FnaOglGoldenImageTests(ITestOutputHelper output)
{
    const string Golden = "fna-ogl-scene.png";

    static FnaOglGoldenImageTests() =>
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "OpenGL");

    [Fact]
    public void Scene_DrawnThroughSkiaRenderTarget2D_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaFnaGlBackend(),
            EngineSkiaGolden.RenderSceneToSkiaTexture, Golden, output);

    [Fact]
    public void Scene_CompositedOntoEngineRenderTarget_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaFnaGlBackend(),
            EngineSkiaGolden.RenderSceneCompositedByEngine, Golden, output);
}
