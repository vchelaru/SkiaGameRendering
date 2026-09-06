using SkiaGameRendering.Kni.DesktopGL;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Kni.DesktopGL;

/// <summary>
/// The KNI counterpart of <c>Tests.DesktopGL</c>'s golden test - same scene, same one-frame game,
/// same reasoning. See <c>MonoGameDesktopGlGoldenImageTests</c>.
/// <para>
/// This is also the only thing that runs <see cref="KniDesktopGlReflectionTests"/>'s pinned members
/// for real. <c>KniGlWrapper</c> resolves them in a static constructor that reads <c>Sdl.Current</c>,
/// which stays null until KNI has created a window, so a live game is what proves the names it pins
/// are reached successfully and not merely present.
/// </para>
/// </summary>
public sealed class KniDesktopGlGoldenImageTests(ITestOutputHelper output)
{
    const string Golden = "kni-desktopgl-scene.png";

    [Fact]
    public void Scene_DrawnThroughSkiaRenderTarget2D_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaKniGlBackend(),
            EngineSkiaGolden.RenderSceneToSkiaTexture, Golden, output);

    [Fact]
    public void Scene_CompositedOntoEngineRenderTarget_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaKniGlBackend(),
            EngineSkiaGolden.RenderSceneCompositedByEngine, Golden, output);
}
