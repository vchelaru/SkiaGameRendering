using SkiaGameRendering.Core.Metal;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using static Tests.CoreMetal.MetalTestNative;

namespace Tests.CoreMetal;

/// <summary>
/// Draws through <c>Core.Metal</c> into a real, host-allocated private-storage <c>MTLTexture</c> on
/// the machine's Metal device, then blits it back to the CPU and checks the pixels. The same shape as
/// <c>VkSkiaPixelReadbackTests</c> and <c>D3D12SkiaPixelReadbackTests</c>; see the
/// headless-gpu-testing skill.
/// </summary>
public sealed class MetalSkiaPixelReadbackTests
{
    [MacOnlyFact]
    public void Clear_WritesExpectedColor_Rgba()
    {
        var expected = new SKColor(10, 20, 30, 255);

        var pixels = RenderAndReadBack(4, 4, MTLPixelFormatRGBA8Unorm, SKColorType.Rgba8888, c => c.Clear(expected));

        Assert.Equal([expected.Red, expected.Green, expected.Blue, expected.Alpha], pixels[..4]);
    }

    /// <summary>BGRA is the other format engines hand over (Godot's screen format, CAMetalLayer's default).</summary>
    [MacOnlyFact]
    public void Clear_WritesExpectedColor_Bgra()
    {
        var expected = new SKColor(10, 20, 30, 255);

        var pixels = RenderAndReadBack(4, 4, MTLPixelFormatBGRA8Unorm, SKColorType.Bgra8888, c => c.Clear(expected));

        Assert.Equal([expected.Blue, expected.Green, expected.Red, expected.Alpha], pixels[..4]);
    }

    /// <summary>
    /// Antialiasing, gradients and blending, compared against a golden rendered on Apple silicon.
    /// There is no software Metal rasterizer to pin to, so a different GPU family could legitimately
    /// differ by more than <see cref="GoldenImage"/>'s tolerance.
    /// </summary>
    [MacOnlyFact]
    public void Scene_MatchesGolden()
    {
        var pixels = RenderAndReadBack(GoldenScene.Width, GoldenScene.Height,
            MTLPixelFormatRGBA8Unorm, SKColorType.Rgba8888, GoldenScene.Draw);

        GoldenImage.AssertMatchesGolden(pixels, "core-metal-scene.png");
    }

    [MacOnlyFact]
    public void CreateTextureState_RejectsTextureWithoutRenderTargetUsage()
    {
        using var metal = new MetalTestDevice();
        using var factory = new MetalSkiaSurfaceFactory();
        factory.InitializeFromNative(metal.Device, metal.Queue);
        var texture = metal.CreateTexture(4, 4, MTLPixelFormatRGBA8Unorm, MTLTextureUsageShaderRead);
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => factory.CreateTextureState(texture));
            Assert.Contains("MTLTextureUsageRenderTarget", ex.Message);
        }
        finally
        {
            objc_release(texture);
        }
    }

    [MacOnlyFact]
    public void InitializeFromNative_RejectsNullAndSwappedHandles()
    {
        using var metal = new MetalTestDevice();
        using var factory = new MetalSkiaSurfaceFactory();

        Assert.Throws<ArgumentException>(() => factory.InitializeFromNative(IntPtr.Zero, metal.Queue));
        Assert.Throws<ArgumentException>(() => factory.InitializeFromNative(metal.Device, IntPtr.Zero));
        Assert.Throws<ArgumentException>(() => factory.InitializeFromNative(metal.Queue, metal.Device));
    }

    static byte[] RenderAndReadBack(int width, int height, nuint pixelFormat, SKColorType colorType, Action<SKCanvas> draw)
    {
        using var metal = new MetalTestDevice();
        var texture = metal.CreateTexture(width, height, pixelFormat, MTLTextureUsageRenderTarget | MTLTextureUsageShaderRead);
        try
        {
            using (var factory = new MetalSkiaSurfaceFactory())
            {
                factory.InitializeFromNative(metal.Device, metal.Queue);
                var state = factory.CreateTextureState(texture);

                factory.BeginDraw();
                var (surface, renderTarget) = factory.CreateSurface(state, width, height, colorType);
                draw(surface.Canvas);
                surface.Flush();
                factory.EndDraw();
                surface.Dispose();
                renderTarget.Dispose();
            }

            return metal.ReadBack(texture, width, height);
        }
        finally
        {
            objc_release(texture);
        }
    }
}
