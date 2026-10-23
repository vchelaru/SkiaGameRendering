using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.DesktopGL;

/// <summary><see cref="SkiaRenderer.DrawToCurrentTarget"/> on MonoGame DesktopGL. See <see cref="DirectDrawLayering"/>.</summary>
public sealed class MonoGameDesktopGlDirectDrawTests(ITestOutputHelper output)
{
    const string Golden = "monogame-desktopgl-scene.png";

    [Fact]
    public void BackBuffer_LayersInOrderAndUpright() =>
        Assert.Equal("", OneFrameGame.Render(() => new SkiaGlBackend(), DirectDrawLayering.BackBuffer));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RenderTarget_LayersInOrderAndUpright(int multiSampleCount) =>
        Assert.Equal("", OneFrameGame.Render(() => new SkiaGlBackend(),
            device => DirectDrawLayering.RenderTarget(device, multiSampleCount)));

    [Fact]
    public void Scene_DrawnToRenderTarget_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaGlBackend(),
            EngineSkiaGolden.RenderSceneDirectToRenderTarget, Golden, output);

    [Fact]
    public void Scene_DrawnToBackBuffer_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaGlBackend(),
            EngineSkiaGolden.RenderSceneDirectToBackBuffer, Golden, output);
}
