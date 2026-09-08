using SkiaGameRendering.Stride.VK;
using Stride.Graphics;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.StrideVK;

/// <summary>
/// Renders <see cref="GoldenScene"/> through <c>SkiaStrideVulkanRenderTarget2D</c> on a real,
/// headless Stride Vulkan <see cref="GraphicsDevice"/> and compares the pixels against a checked-in
/// reference. Vulkan analog of <c>Tests.Stride.D3D11</c>'s <c>StrideGoldenImageTests</c> - see that
/// class for why a whole-image comparison covers what
/// <see cref="StrideVulkanCompositeColorTests"/>'s solid color can't, and
/// <see cref="StrideSkiaGolden"/> for the color space these run on.
/// <para>
/// Unlike the D3D11 backend, this one has no software-rasterizer switch of its own: Stride's Vulkan
/// device comes from whatever ICD the loader finds, which is Mesa lavapipe on CI (registered under
/// <c>HKLM:\SOFTWARE\Khronos\Vulkan\Drivers</c> by <c>master.yml</c>) and the machine's real driver
/// on a dev box. Those rasterize antialiased edges differently enough that comparing a dev box's
/// render against CI's golden would fail for reasons that are not regressions, so the pixel
/// comparison only runs under <c>SKIAGAMERENDERING_PINNED_RASTERIZER</c> - the same gate
/// <c>Tests.Core.VK</c>'s golden uses. The render, and its orientation, are still checked either way.
/// </para>
/// </summary>
public sealed class StrideVulkanGoldenImageTests : IDisposable
{
    const string Golden = "stride-vk-scene.png";

    readonly ITestOutputHelper _output;
    readonly GraphicsDevice _graphicsDevice;

    public StrideVulkanGoldenImageTests(ITestOutputHelper output)
    {
        _output = output;
        _graphicsDevice = GraphicsDevice.New();
        _graphicsDevice.ColorSpace = ColorSpace.Gamma;
    }

    public void Dispose()
    {
        SkiaStrideVulkanRenderer.Dispose();
        _graphicsDevice.Dispose();
    }

    [Fact]
    public void Scene_DrawnThroughSkiaStrideVulkanRenderTarget2D_MatchesGolden() =>
        AssertMatchesGolden(StrideSkiaGolden.RenderSceneToSkiaTexture(_graphicsDevice));

    [Fact]
    public void Scene_CompositedOntoStrideRenderTarget_MatchesGolden() =>
        AssertMatchesGolden(StrideSkiaGolden.RenderSceneCompositedByEngine(_graphicsDevice));

    void AssertMatchesGolden(byte[] rgba)
    {
        GoldenImage.AssertSceneOrientation(rgba);
        if (GoldenImage.PinnedRasterizerInUse)
            GoldenImage.AssertMatchesGolden(rgba, Golden);
        else
            _output.WriteLine("Golden comparison skipped: the golden was rendered by CI's pinned Mesa " +
                "lavapipe build and this run used whichever Vulkan driver this machine has. The render " +
                "itself still ran and was checked for orientation.");
    }
}
