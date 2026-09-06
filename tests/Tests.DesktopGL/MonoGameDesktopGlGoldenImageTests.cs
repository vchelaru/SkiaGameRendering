using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.DesktopGL;

/// <summary>
/// Renders <see cref="GoldenScene"/> through <c>SkiaRenderTarget2D</c> on a real MonoGame DesktopGL
/// <c>GraphicsDevice</c> and compares the pixels against a checked-in reference. The OpenGL
/// counterpart of <c>Tests.WindowsDX</c>'s golden test, and the first thing in this repo to render
/// anything at all through <see cref="SkiaGlBackend"/> - it had no test project before this one.
/// <para>
/// Unlike the WindowsDX tests, this needs a running <c>Game</c>: MonoGame takes its GL context from
/// the SDL window. See <see cref="OneFrameGame"/>.
/// </para>
/// </summary>
public sealed class MonoGameDesktopGlGoldenImageTests(ITestOutputHelper output)
{
    const string Golden = "monogame-desktopgl-scene.png";

    [Fact]
    public void Scene_DrawnThroughSkiaRenderTarget2D_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaGlBackend(),
            EngineSkiaGolden.RenderSceneToSkiaTexture, Golden, output);

    [Fact]
    public void Scene_CompositedOntoEngineRenderTarget_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaGlBackend(),
            EngineSkiaGolden.RenderSceneCompositedByEngine, Golden, output);
}
