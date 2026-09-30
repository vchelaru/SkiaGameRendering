using SkiaGameRendering.Stride.D3D12;
using Stride.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.StrideD3D12;

/// <summary>
/// Renders <see cref="GoldenScene"/> through <c>SkiaStrideD3D12RenderTarget2D</c> on a real, headless
/// Stride D3D12 <see cref="GraphicsDevice"/> on WARP and compares the pixels against a checked-in
/// reference. D3D12 analog of <c>Tests.Stride.D3D11</c>'s <c>StrideGoldenImageTests</c>; Skia draws on
/// Stride's own device, so both halves rasterize on WARP and no pinned-rasterizer gate is needed.
/// </summary>
public sealed class StrideD3D12GoldenImageTests : IDisposable
{
    const string Golden = "stride-d3d12-scene.png";

    readonly GraphicsDevice _graphicsDevice = WarpStrideDevice.Create(ColorSpace.Gamma);

    public void Dispose()
    {
        SkiaStrideD3D12Renderer.Dispose();
        _graphicsDevice.Dispose();
    }

    [Fact]
    public void Scene_DrawnThroughSkiaStrideD3D12RenderTarget2D_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(StrideSkiaGolden.RenderSceneToSkiaTexture(_graphicsDevice), Golden);

    [Fact]
    public void Scene_CompositedOntoStrideRenderTarget_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(StrideSkiaGolden.RenderSceneCompositedByEngine(_graphicsDevice), Golden);
}
