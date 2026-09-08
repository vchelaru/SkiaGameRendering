using SkiaGameRendering.Stride.D3D11;
using Stride.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.Stride;

/// <summary>
/// Renders <see cref="GoldenScene"/> through <c>SkiaStrideRenderTarget2D</c> on a real, headless
/// Stride D3D11 <see cref="GraphicsDevice"/> and compares the pixels against a checked-in reference.
/// <para>
/// <see cref="StrideCompositeColorTests"/> already proves a solid color survives the composite at
/// 8x8, which catches the sRGB double-encode class of bug and nothing else. Antialiased edges, a
/// gradient and overlapping translucent fills are what tell a working Skia GPU pipeline from one
/// that silently lost its blending or fell back to a different rasterization path, and that needs a
/// whole image to compare.
/// </para>
/// <para>
/// Both tests share one golden: a Stride composite of an opaque scene must be pixel-identical to
/// the Skia texture it came from. See <see cref="StrideSkiaGolden"/> for why they run on a
/// <see cref="ColorSpace.Gamma"/> device.
/// </para>
/// <para>
/// <c>STRIDE_GRAPHICS_SOFTWARE_RENDERING=1</c> pins the device to WARP the same way
/// <see cref="StrideCompositeColorTests"/> does, and the ANGLE context Skia draws through is built
/// on that same device (<c>SkiaStrideContext.Initialize</c> takes Stride's own
/// <c>NativeDevice</c>), so both halves of this test rasterize on WARP wherever it runs. That makes
/// the golden comparable on a dev box with a real GPU as well as on CI, with no
/// <c>SKIAGAMERENDERING_PINNED_RASTERIZER</c> gate - unlike the Vulkan backend's golden.
/// </para>
/// </summary>
public sealed class StrideGoldenImageTests : IDisposable
{
    const string Golden = "stride-d3d11-scene.png";

    readonly GraphicsDevice _graphicsDevice;

    public StrideGoldenImageTests()
    {
        Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", "1");
        try
        {
            _graphicsDevice = GraphicsDevice.New();
        }
        catch
        {
            Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", null);
            throw;
        }

        _graphicsDevice.ColorSpace = ColorSpace.Gamma;
    }

    public void Dispose()
    {
        SkiaStrideRenderer.Dispose();
        _graphicsDevice.Dispose();
        Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", null);
    }

    [Fact]
    public void Scene_DrawnThroughSkiaStrideRenderTarget2D_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(StrideSkiaGolden.RenderSceneToSkiaTexture(_graphicsDevice), Golden);

    [Fact]
    public void Scene_CompositedOntoStrideRenderTarget_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(StrideSkiaGolden.RenderSceneCompositedByEngine(_graphicsDevice), Golden);
}
