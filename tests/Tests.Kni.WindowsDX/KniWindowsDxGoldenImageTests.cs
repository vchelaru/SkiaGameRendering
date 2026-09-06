using SkiaGameRendering;
using SkiaGameRendering.Kni.WindowsDX;
using Tests.Shared;
using Xunit;

namespace Tests.Kni.WindowsDX;

/// <summary>
/// The KNI counterpart of <c>Tests.WindowsDX</c>'s golden test - same scene, same headless device
/// setup, same reasoning. See <c>MonoGameWindowsDxGoldenImageTests</c>.
/// <para>
/// KNI gets its own golden rather than sharing MonoGame's: both end up in Skia on ANGLE over WARP,
/// but nothing guarantees the two engines hand it identical texture and blend setup, and a shared
/// golden would quietly hide the difference if they ever diverged.
/// </para>
/// </summary>
public sealed class KniWindowsDxGoldenImageTests : IDisposable
{
    const string Golden = "kni-windowsdx-scene.png";

    readonly HeadlessGraphicsDevice _headless = new(GoldenScene.Width, GoldenScene.Height);

    public KniWindowsDxGoldenImageTests() =>
        SkiaRenderer.Initialize(new SkiaKniAngleBackend(), _headless.GraphicsDevice);

    public void Dispose()
    {
        SkiaRenderer.Dispose();
        _headless.Dispose();
    }

    [Fact]
    public void Scene_DrawnThroughSkiaRenderTarget2D_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            EngineSkiaGolden.RenderSceneToSkiaTexture(_headless.GraphicsDevice), Golden);

    [Fact]
    public void Scene_CompositedOntoEngineRenderTarget_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            EngineSkiaGolden.RenderSceneCompositedByEngine(_headless.GraphicsDevice), Golden);
}
