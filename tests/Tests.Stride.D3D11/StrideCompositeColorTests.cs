using SkiaGameRendering.Stride.D3D11;
using SkiaSharp;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Xunit;

namespace Tests.Stride;

/// <summary>
/// D3D11 analog of <c>Tests.Stride.VK</c>'s <c>StrideVulkanCompositeColorTests</c> - see that class's
/// doc comment for the full mechanism (Stride's default Linear color-space pipeline, its sRGB-format
/// back buffer, and why an uncompensated composite double-encodes) and
/// <c>SkiaStrideTarget</c>/<c>SkiaStrideVulkanTarget</c>'s doc comments for the fix. Same shape: a
/// real (no window, no <c>Game</c>) headless Stride D3D11 <see cref="GraphicsDevice"/>, an offscreen
/// sRGB-formatted render target standing in for the real presented back buffer, a real
/// <see cref="SkiaStrideRenderTarget2D"/> drawing a known solid color, composited via the adapter's
/// own <c>SpriteBatch</c> path, read back to CPU and compared against what was drawn.
/// <para>
/// <c>STRIDE_GRAPHICS_SOFTWARE_RENDERING=1</c> is Stride's own built-in switch (decompiled and
/// confirmed in <c>Stride.Graphics.GraphicsAdapterFactory</c>, not assumed) for forcing
/// <c>IDXGIFactory4.EnumWarpAdapter</c> instead of enumerating real hardware adapters - the
/// Stride-level equivalent of <c>Tests.Core.ANGLE</c>'s raw <c>WarpDevice</c> helper, giving this
/// test the same "runs on GitHub's GPU-less windows-latest runner" property with no extra CI wiring,
/// and the same deterministic software-rasterizer behavior on a dev box with a real GPU.
/// </para>
/// </summary>
public sealed class StrideCompositeColorTests
{
    [Fact]
    public void CrimsonRectangle_CompositesToExpectedColor_ThroughRealStrideD3D11Device()
    {
        const int width = 8;
        const int height = 8;
        var crimson = new SKColor(220, 20, 60, 255);

        Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", "1");
        try
        {
            using var device = GraphicsDevice.New();
            // Stride.Games.GraphicsDeviceManager's own default (see SkiaStrideTarget's doc comment) -
            // set explicitly here since this test builds a device directly, without going through
            // GraphicsDeviceManager at all.
            device.ColorSpace = ColorSpace.Linear;

            // Stands in for the real presented back buffer: under Stride's default Linear pipeline,
            // GraphicsDeviceManager creates the actual back buffer with this same sRGB-variant format
            // (PixelFormatExtensions.ToSRgb applied to PreferredBackBufferFormat) - see
            // SkiaStrideTarget's doc comment for the decompiled evidence.
            using var compositeTarget = Texture.New2D(
                device, width, height, PixelFormat.R8G8B8A8_UNorm_SRgb,
                TextureFlags.RenderTarget | TextureFlags.ShaderResource);

            var canvas = new SkiaStrideRenderTarget2D(device, width, height);

            // Real GraphicsCompositor/RenderDrawContext machinery is Game-only; standing up the same
            // shape by hand (a GraphicsContext with compositeTarget bound as its CommandList's render
            // target) is enough for canvas.End's own SpriteBatch composite to run against - the exact
            // code path SkiaStrideRenderTarget2D.End (and SkiaStrideSceneRenderer.DrawCore, in the
            // real sample) drives every frame. D3D11 only supports one CommandList per device (it
            // wraps the immediate context, not a deferred one) - explicit CommandList.New throws
            // "Creation of additional Command Lists is not supported for Direct3D 11", so this passes
            // commandList: null and lets GraphicsContext resolve GraphicsDevice.InternalMainCommandList
            // itself instead (the same thing a real RenderDrawContext.GraphicsContext does).
            //
            // Bound BEFORE canvas.Begin(), not after: SkiaStrideRenderTarget2D.Begin swaps the
            // engine's D3D11 device-context state OUT to ANGLE's own state (Core.ANGLE's
            // SwapDeviceContextState dance - see AngleSkiaSurfaceFactory.BeginDraw), and End swaps it
            // back in right before the SpriteBatch composite runs. Binding compositeTarget beforehand
            // means it's part of the "saved" engine state End restores; binding it after Begin would
            // set it against ANGLE's swapped-out state instead, where the composite never sees it.
            var graphicsContext = new GraphicsContext(device);
            var commandList = graphicsContext.CommandList;
            commandList.SetRenderTargetAndViewport(null, compositeTarget);
            commandList.Clear(compositeTarget, new Color4(0f, 0f, 0f, 0f));

            canvas.Begin();
            canvas.Canvas.Clear(crimson);
            canvas.End(graphicsContext);

            var pixels = compositeTarget.GetData<Color>(commandList);

            canvas.Dispose();

            var actual = pixels[0];

            // See StrideVulkanCompositeColorTests for why this tolerance and not an exact match.
            const int tolerance = 3;
            Assert.True(Math.Abs(actual.R - crimson.Red) <= tolerance, $"R: expected {crimson.Red}, got {actual.R}");
            Assert.True(Math.Abs(actual.G - crimson.Green) <= tolerance, $"G: expected {crimson.Green}, got {actual.G}");
            Assert.True(Math.Abs(actual.B - crimson.Blue) <= tolerance, $"B: expected {crimson.Blue}, got {actual.B}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", null);
        }
    }
}
