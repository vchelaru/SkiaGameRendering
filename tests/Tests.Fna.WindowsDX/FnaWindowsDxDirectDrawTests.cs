using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.Fna.WindowsDX;

/// <summary>
/// <see cref="SkiaDirectCanvas"/> on FNA's D3D11 driver. See <see cref="DirectDrawLayering"/>. FNA3D draws
/// into a back buffer texture of its own and copies it to the swap chain on present, so "the back buffer"
/// here is that texture. Same environment as <see cref="FnaWindowsDxGoldenImageTests"/>.
/// <para>
/// No <see cref="DirectDrawLayering.BackBufferAfterResize"/> here: inside <see cref="OneFrameGame"/>, FNA3D
/// throws from the frame's present after any SpriteBatch draw followed by a <c>Reset</c>, with or without Skia.
/// The back buffer FNA3D resizes is its own texture rather than a swap chain buffer anyway.
/// </para>
/// </summary>
public sealed class FnaWindowsDxDirectDrawTests
{
    const string Golden = "fna-windowsdx-scene.png";

    static FnaWindowsDxDirectDrawTests()
    {
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "D3D11");
        Environment.SetEnvironmentVariable("FNA3D_D3D11_USE_WARP", "1");
    }

    [Fact]
    public void SupportsDirectCanvas() =>
        Assert.True(OneFrameGame.Render(() => new SkiaAngleBackend(), _ => (object)SkiaRenderer.SupportsDirectCanvas) is true);

    [Fact]
    public void BackBuffer_LayersInOrderAndUpright() =>
        Assert.Equal("", OneFrameGame.Render(() => new SkiaAngleBackend(), DirectDrawLayering.BackBuffer));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RenderTarget_LayersInOrderAndUpright(int multiSampleCount) =>
        Assert.Equal("", OneFrameGame.Render(() => new SkiaAngleBackend(),
            device => DirectDrawLayering.RenderTarget(device, multiSampleCount)));

    [Fact]
    public void Scene_DrawnToRenderTarget_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            OneFrameGame.Render(() => new SkiaAngleBackend(), EngineSkiaGolden.RenderSceneDirectToRenderTarget),
            Golden);

    [Fact]
    public void Scene_DrawnToBackBuffer_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            OneFrameGame.Render(() => new SkiaAngleBackend(), EngineSkiaGolden.RenderSceneDirectToBackBuffer),
            Golden);

    [Fact]
    public void SkiaRenderTarget2D_AroundDirectDraw_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(
            OneFrameGame.Render(() => new SkiaAngleBackend(), EngineSkiaGolden.RenderSceneToSkiaTextureAroundDirectDraw),
            Golden);
}
