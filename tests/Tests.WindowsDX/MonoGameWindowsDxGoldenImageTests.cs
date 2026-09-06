using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.WindowsDX;

/// <summary>
/// Renders <see cref="GoldenScene"/> through <c>SkiaRenderTarget2D</c> on a real, headless MonoGame
/// WindowsDX <see cref="Microsoft.Xna.Framework.Graphics.GraphicsDevice"/> (see
/// <see cref="HeadlessGraphicsDevice"/>) and compares the pixels against a checked-in reference.
/// <para>
/// <c>Tests.Core.ANGLE</c> covers the Skia-to-D3D11 half; <see cref="MonoGameWindowsDxReflectionTests"/>
/// covers the names <c>SkiaAngleBackend</c> reaches by string. Neither renders anything through
/// MonoGame itself, which left the engine glue - texture allocation, the shared-texture handoff,
/// the state around the draw - verified only by a human running the sample.
/// </para>
/// <para>
/// Both tests share one golden: an engine composite of an opaque scene must be pixel-identical to
/// the Skia texture it came from.
/// </para>
/// </summary>
public sealed class MonoGameWindowsDxGoldenImageTests : IDisposable
{
    const string Golden = "monogame-windowsdx-scene.png";

    readonly HeadlessGraphicsDevice _headless = new(GoldenScene.Width, GoldenScene.Height);

    public MonoGameWindowsDxGoldenImageTests() =>
        SkiaRenderer.Initialize(new SkiaAngleBackend(), _headless.GraphicsDevice);

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
