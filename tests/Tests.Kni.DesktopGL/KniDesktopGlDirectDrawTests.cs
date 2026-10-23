using SkiaGameRendering;
using SkiaGameRendering.Kni.DesktopGL;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Kni.DesktopGL;

/// <summary><see cref="SkiaRenderer.DrawToCurrentTarget"/> on KNI DesktopGL. See <see cref="DirectDrawLayering"/>.</summary>
public sealed class KniDesktopGlDirectDrawTests(ITestOutputHelper output)
{
    const string Golden = "kni-desktopgl-scene.png";

    [Fact]
    public void BackBuffer_LayersInOrderAndUpright() =>
        Assert.Equal("", OneFrameGame.Render(() => new SkiaKniGlBackend(), DirectDrawLayering.BackBuffer));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RenderTarget_LayersInOrderAndUpright(int multiSampleCount) =>
        Assert.Equal("", OneFrameGame.Render(() => new SkiaKniGlBackend(),
            device => DirectDrawLayering.RenderTarget(device, multiSampleCount)));

    [Fact]
    public void Scene_DrawnToRenderTarget_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaKniGlBackend(),
            EngineSkiaGolden.RenderSceneDirectToRenderTarget, Golden, output);

    [Fact]
    public void Scene_DrawnToBackBuffer_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaKniGlBackend(),
            EngineSkiaGolden.RenderSceneDirectToBackBuffer, Golden, output);
}
