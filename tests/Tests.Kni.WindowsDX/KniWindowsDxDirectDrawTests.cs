using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.Kni.WindowsDX;

/// <summary><see cref="SkiaDirectCanvas"/> on KNI WindowsDX. See <see cref="DirectDrawLayering"/>.</summary>
[Collection(SkiaRendererCollection.Name)]
public sealed class KniWindowsDxDirectDrawTests : IDisposable
{
    const string Golden = "kni-windowsdx-scene.png";

    readonly HeadlessGraphicsDevice _headless = new(GoldenScene.Width, GoldenScene.Height);

    public KniWindowsDxDirectDrawTests() =>
        SkiaRenderer.Initialize(new SkiaAngleBackend(), _headless.GraphicsDevice);

    public void Dispose()
    {
        SkiaRenderer.Dispose();
        _headless.Dispose();
    }

    [Fact]
    public void SupportsDirectCanvas() => Assert.True(SkiaRenderer.SupportsDirectCanvas);

    [Fact]
    public void BackBuffer_LayersInOrderAndUpright() =>
        Assert.Equal("", DirectDrawLayering.BackBuffer(_headless.GraphicsDevice));

    [Fact]
    public void BackBuffer_AfterResize_LayersInOrderAndUpright() =>
        Assert.Equal("", DirectDrawLayering.BackBufferAfterResize(_headless.GraphicsDevice));

    [Fact]
    public void MultisampledBackBuffer_LayersInOrderAndUpright()
    {
        SkiaRenderer.Dispose();
        using var headless = new HeadlessGraphicsDevice(GoldenScene.Width, GoldenScene.Height, multiSampleCount: 4);
        SkiaRenderer.Initialize(new SkiaAngleBackend(), headless.GraphicsDevice);
        try
        {
            Assert.Equal(4, headless.GraphicsDevice.PresentationParameters.MultiSampleCount);
            Assert.Equal("", DirectDrawLayering.BackBuffer(headless.GraphicsDevice));
        }
        finally
        {
            SkiaRenderer.Dispose();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RenderTarget_LayersInOrderAndUpright(int multiSampleCount) =>
        Assert.Equal("", DirectDrawLayering.RenderTarget(_headless.GraphicsDevice, multiSampleCount));

    [Fact]
    public void Scene_DrawnToRenderTarget_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            EngineSkiaGolden.RenderSceneDirectToRenderTarget(_headless.GraphicsDevice), Golden);

    [Fact]
    public void Scene_DrawnToBackBuffer_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            EngineSkiaGolden.RenderSceneDirectToBackBuffer(_headless.GraphicsDevice), Golden);

    /// <summary>A direct pass leaves ANGLE's cached D3D11 state consistent for a SkiaRenderTarget2D drawn after it.</summary>
    [Fact]
    public void SkiaRenderTarget2D_AroundDirectDraw_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            EngineSkiaGolden.RenderSceneToSkiaTextureAroundDirectDraw(_headless.GraphicsDevice), Golden);
}
