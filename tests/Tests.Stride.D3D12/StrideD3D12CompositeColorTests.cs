using SkiaGameRendering.Stride.D3D12;
using SkiaSharp;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Xunit;

namespace Tests.StrideD3D12;

/// <summary>
/// Solid-color round trips through <see cref="SkiaStrideD3D12RenderTarget2D"/>'s own
/// <c>SpriteBatch</c> composite on a headless Stride D3D12 device on WARP.
/// </summary>
public sealed class StrideD3D12CompositeColorTests : IDisposable
{
    const int Size = 8;

    GraphicsDevice? _device;

    public void Dispose()
    {
        // The renderer's context is static and pinned to this test's device.
        SkiaStrideD3D12Renderer.Dispose();
        _device?.Dispose();
    }

    /// <summary>
    /// The sRGB double-encode regression <c>Tests.Stride.VK</c>'s <c>StrideVulkanCompositeColorTests</c>
    /// covers: Stride's default Linear pipeline, an sRGB-formatted target standing in for the back
    /// buffer, and crimson must come out crimson rather than pink.
    /// </summary>
    [Fact]
    public void CrimsonRectangle_CompositesToExpectedColor_UnderLinearColorSpace()
    {
        var crimson = new SKColor(220, 20, 60, 255);
        _device = WarpStrideDevice.Create(ColorSpace.Linear);

        using var compositeTarget = NewTarget(PixelFormat.R8G8B8A8_UNorm_SRgb);
        using var canvas = new SkiaStrideD3D12RenderTarget2D(_device, Size, Size);

        DrawAndComposite(canvas, compositeTarget, crimson);

        AssertColor(crimson, ReadFirstPixel(compositeTarget), tolerance: 3);
    }

    /// <summary>
    /// The composite leaves the Skia texture in Stride's <c>ShaderResource</c> layout, while Skia still
    /// believes it is a render target. The second frame only lands if the adapter moves it back
    /// before Skia draws; otherwise the texture keeps frame one's color (or the device is removed).
    /// </summary>
    [Fact]
    public void SecondFrame_AfterComposite_ReplacesFirstFramesColor()
    {
        _device = WarpStrideDevice.Create(ColorSpace.Gamma);

        using var compositeTarget = NewTarget(PixelFormat.R8G8B8A8_UNorm);
        using var canvas = new SkiaStrideD3D12RenderTarget2D(_device, Size, Size);

        DrawAndComposite(canvas, compositeTarget, SKColors.Red);
        AssertColor(SKColors.Red, ReadFirstPixel(compositeTarget), tolerance: 0);

        DrawAndComposite(canvas, compositeTarget, SKColors.Blue);
        AssertColor(SKColors.Blue, ReadFirstPixel(compositeTarget), tolerance: 0);
        AssertColor(SKColors.Blue, ReadFirstPixel(canvas.Texture), tolerance: 0);
    }

    Texture NewTarget(PixelFormat format) =>
        Texture.New2D(_device!, Size, Size, format, TextureFlags.RenderTarget | TextureFlags.ShaderResource);

    /// <summary>The shape <c>SkiaStrideD3D12SceneRenderer.DrawCore</c> drives each frame, with the compositor's command list built by hand.</summary>
    void DrawAndComposite(SkiaStrideD3D12RenderTarget2D canvas, Texture compositeTarget, SKColor color)
    {
        using var commandList = CommandList.New(_device!);
        commandList.SetRenderTargetAndViewport(null, compositeTarget);
        commandList.Clear(compositeTarget, new Color4(0f, 0f, 0f, 0f));

        canvas.Begin();
        canvas.Canvas.Clear(color);
        canvas.End(new GraphicsContext(_device!, commandList: commandList));

        _device!.ExecuteCommandList(commandList.Close());
    }

    Color ReadFirstPixel(Texture texture)
    {
        using var commandList = CommandList.New(_device!);
        var pixels = texture.GetData<Color>(commandList);
        _device!.ExecuteCommandList(commandList.Close());
        return pixels[0];
    }

    static void AssertColor(SKColor expected, Color actual, int tolerance)
    {
        Assert.True(Math.Abs(actual.R - expected.Red) <= tolerance, $"R: expected {expected.Red}, got {actual.R}");
        Assert.True(Math.Abs(actual.G - expected.Green) <= tolerance, $"G: expected {expected.Green}, got {actual.G}");
        Assert.True(Math.Abs(actual.B - expected.Blue) <= tolerance, $"B: expected {expected.Blue}, got {actual.B}");
    }
}
