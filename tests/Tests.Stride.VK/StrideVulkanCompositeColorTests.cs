using SkiaGameRendering.Stride.VK;
using SkiaSharp;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Xunit;

namespace Tests.StrideVK;

/// <summary>
/// End-to-end regression test for the "Sample.Stride.VK draws crimson but shows pink" bug: a real
/// (no window, no <c>Game</c>, no <c>GraphicsCompositor</c>) headless Stride Vulkan
/// <see cref="GraphicsDevice"/>, an offscreen sRGB-formatted render target standing in for the real
/// presented back buffer Stride's default Linear color-space pipeline creates (see
/// <c>SkiaStrideVulkanTarget</c>'s doc comment for the full mechanism), a real
/// <see cref="SkiaStrideVulkanRenderTarget2D"/> drawing a solid known color, composited via the
/// adapter's own <c>SpriteBatch</c> composite path, then read back to CPU and compared against the
/// color that was drawn.
/// <para>
/// <c>GraphicsDevice.New(...)</c> is a genuine public, <c>Game</c>-free Stride factory (used
/// internally by <c>Stride.Games.GraphicsDeviceManager</c> and by Stride's own tooling) - it enumerates
/// physical devices the same way <c>Tests.Core.VK</c>'s raw Vulkan device already does, so the same
/// Mesa lavapipe registry-ICD registration <c>master.yml</c> performs for that project's CI job makes
/// this test's device creation succeed there too, with no extra CI wiring. Locally, it uses whatever
/// real Vulkan driver is already installed - see the headless-gpu-testing skill.
/// </para>
/// </summary>
public sealed class StrideVulkanCompositeColorTests
{
    [Fact]
    public void CrimsonRectangle_CompositesToExpectedColor_ThroughRealStrideVulkanDevice()
    {
        const int width = 8;
        const int height = 8;
        var crimson = new SKColor(220, 20, 60, 255);

        using var device = GraphicsDevice.New();
        // Stride.Games.GraphicsDeviceManager's own default (see SkiaStrideVulkanTarget's doc
        // comment) - set explicitly here since this test builds a device directly, without going
        // through GraphicsDeviceManager at all.
        device.ColorSpace = ColorSpace.Linear;

        // Stands in for the real presented back buffer: under Stride's default Linear pipeline,
        // GraphicsDeviceManager creates the actual back buffer with this same sRGB-variant format
        // (PixelFormatExtensions.ToSRgb applied to PreferredBackBufferFormat) - see
        // SkiaStrideVulkanTarget's doc comment for the decompiled evidence.
        using var compositeTarget = Texture.New2D(
            device, width, height, PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        using var canvas = new SkiaStrideVulkanRenderTarget2D(device, width, height);

        canvas.Begin();
        canvas.Canvas.Clear(crimson);

        // Real GraphicsCompositor/RenderDrawContext machinery is Game-only; standing up the same
        // shape by hand (a CommandList with compositeTarget bound, wrapped in a GraphicsContext) is
        // enough for canvas.End's own SpriteBatch composite to run against - the exact code path
        // SkiaStrideVulkanRenderTarget2D.End (and SkiaStrideVulkanSceneRenderer.DrawCore, in the real
        // sample) drives every frame.
        using var commandList = CommandList.New(device);
        commandList.SetRenderTargetAndViewport(null, compositeTarget);
        commandList.Clear(compositeTarget, new Color4(0f, 0f, 0f, 0f));

        var graphicsContext = new GraphicsContext(device, commandList: commandList);
        canvas.End(graphicsContext);

        device.ExecuteCommandList(commandList.Close());

        using var readbackCommandList = CommandList.New(device);
        var pixels = compositeTarget.GetData<Color>(readbackCommandList);
        device.ExecuteCommandList(readbackCommandList.Close());

        var actual = pixels[0];

        // A few-ULP tolerance for the sRGB decode/encode round trip through 8-bit storage twice
        // (once in this class's texture, once in compositeTarget) plus whatever Skia/GPU rounding
        // happens along the way - not an exact bit-for-bit match, but nowhere near the ~19-unit
        // shift the uncompensated double-encode bug produces (220 -> ~239, 20 -> ~79, 60 -> ~133).
        const int tolerance = 3;
        Assert.True(Math.Abs(actual.R - crimson.Red) <= tolerance, $"R: expected {crimson.Red}, got {actual.R}");
        Assert.True(Math.Abs(actual.G - crimson.Green) <= tolerance, $"G: expected {crimson.Green}, got {actual.G}");
        Assert.True(Math.Abs(actual.B - crimson.Blue) <= tolerance, $"B: expected {crimson.Blue}, got {actual.B}");
    }
}
