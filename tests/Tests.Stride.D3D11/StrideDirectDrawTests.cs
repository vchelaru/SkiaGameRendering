using SkiaGameRendering.Stride.D3D11;
using SkiaSharp;
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.Stride;

/// <summary>
/// <see cref="SkiaStrideDirectCanvas"/> on a WARP Stride device. Each case draws a Stride sprite, then Skia,
/// then another Stride sprite into whatever is bound, and reads the target back: a flip, a channel swap,
/// Skia landing out of order, or Stride's cached state going stale after the Skia pass each fails a probe.
/// The Stride colors only use 0 and 255 channels, which read back the same through an sRGB view.
/// </summary>
public sealed class StrideDirectDrawTests : IDisposable
{
    const int Size = 64;
    const string Golden = "stride-d3d11-scene.png";

    static readonly Color Background = new(0, 0, 255, 255);
    static readonly Color Before = new(0, 255, 0, 255);
    static readonly SKColor Skia = new(210, 50, 90);
    static readonly Color After = new(255, 255, 0, 255);

    readonly GraphicsDevice _graphicsDevice;

    public StrideDirectDrawTests()
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
    }

    public void Dispose()
    {
        SkiaStrideRenderer.Dispose();
        _graphicsDevice.Dispose();
        Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", null);
    }

    /// <summary>
    /// The swap chain back buffer, as a Stride game binds it inside its compositor. In Stride's default
    /// Linear color space that is an sRGB view of a UNORM flip-model buffer.
    /// </summary>
    [Theory]
    [InlineData(ColorSpace.Linear, PixelFormat.R8G8B8A8_UNorm_SRgb)]
    [InlineData(ColorSpace.Gamma, PixelFormat.R8G8B8A8_UNorm)]
    [InlineData(ColorSpace.Gamma, PixelFormat.B8G8R8A8_UNorm)]
    public void BackBuffer_LayersInOrderAndUpright(ColorSpace colorSpace, PixelFormat format)
    {
        _graphicsDevice.ColorSpace = colorSpace;
        using var window = new HiddenWindow(Size, Size);
        var parameters = new PresentationParameters(Size, Size,
            new WindowHandle(AppContextType.DesktopWinForms, null!, window.Handle), format);
        using var presenter = new SwapChainGraphicsPresenter(_graphicsDevice, parameters);
        _graphicsDevice.Presenter = presenter;
        Assert.Equal(format, presenter.BackBuffer.ViewFormat);
        try
        {
            Assert.Equal("", DrawAndCheck(presenter.BackBuffer));
        }
        finally
        {
            _graphicsDevice.Presenter = null!;
        }
    }

    [Theory]
    [InlineData(PixelFormat.R8G8B8A8_UNorm)]
    [InlineData(PixelFormat.B8G8R8A8_UNorm)]
    public void RenderTarget_LayersInOrderAndUpright(PixelFormat format)
    {
        _graphicsDevice.ColorSpace = ColorSpace.Gamma;
        using var target = Texture.New2D(_graphicsDevice, Size, Size, format,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        Assert.Equal("", DrawAndCheck(target));
    }

    /// <summary>ANGLE would encode Skia's writes into an sRGB-typed texture, and Stride's HDR targets aren't 8-bit.</summary>
    [Theory]
    [InlineData(PixelFormat.R8G8B8A8_UNorm_SRgb)]
    [InlineData(PixelFormat.R16G16B16A16_Float)]
    public void UnsupportedFormat_ThrowsNotSupported_AndCanStillDraw(PixelFormat format)
    {
        using var target = Texture.New2D(_graphicsDevice, Size, Size, format,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        var graphicsContext = new GraphicsContext(_graphicsDevice);
        graphicsContext.CommandList.SetRenderTargetAndViewport(null, target);

        using var direct = new SkiaStrideDirectCanvas(_graphicsDevice);
        Assert.Throws<NotSupportedException>(() => direct.Begin(graphicsContext));

        // A refused Begin leaves no pass open.
        Assert.Throws<InvalidOperationException>(() => direct.End());
        using var supported = Texture.New2D(_graphicsDevice, Size, Size, PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        graphicsContext.CommandList.SetRenderTargetAndViewport(null, supported);
        direct.Begin(graphicsContext);
        direct.End();
    }

    [Fact]
    public void NothingBound_Throws()
    {
        var graphicsContext = new GraphicsContext(_graphicsDevice);
        graphicsContext.CommandList.SetRenderTargets(null, ReadOnlySpan<Texture>.Empty);
        using var direct = new SkiaStrideDirectCanvas(_graphicsDevice);
        Assert.Throws<InvalidOperationException>(() => direct.Begin(graphicsContext));
    }

    [Fact]
    public void Scene_DrawnToRenderTarget_MatchesGolden()
    {
        _graphicsDevice.ColorSpace = ColorSpace.Gamma;
        GoldenImage.AssertMatchesGolden(StrideSkiaGolden.RenderSceneDirect(_graphicsDevice), Golden);
    }

    /// <summary>Draws the layers into <paramref name="target"/> and reports every probe that is wrong, or "".</summary>
    string DrawAndCheck(Texture target)
    {
        using var white = Texture.New2D(_graphicsDevice, 1, 1, PixelFormat.R8G8B8A8_UNorm, new[] { Color.White });
        using var spriteBatch = new SpriteBatch(_graphicsDevice);
        using var direct = new SkiaStrideDirectCanvas(_graphicsDevice);
        using var paint = new SKPaint { Color = Skia };

        var graphicsContext = new GraphicsContext(_graphicsDevice);
        var commandList = graphicsContext.CommandList;
        commandList.SetRenderTargetAndViewport(null, target);
        commandList.Clear(target, (Color4)Background);

        // Before: the top-left quadrant.
        spriteBatch.Begin(graphicsContext);
        spriteBatch.Draw(white, new RectangleF(0, 0, Size / 2, Size / 2), (Color4)Before);
        spriteBatch.End();

        // Skia: a strip along the top, across the middle half. Twice, so the second pass reuses ANGLE's
        // cached state from the first.
        for (var i = 0; i < 2; i++)
        {
            direct.Begin(graphicsContext);
            direct.Canvas.DrawRect(Size / 4, 0, Size / 2, Size / 4, paint);
            direct.End();
        }

        // After: the right half, from 1/8 down to 1/2. Stride's cached state has to still match D3D11's.
        spriteBatch.Begin(graphicsContext);
        spriteBatch.Draw(white, new RectangleF(Size / 2, Size / 8, Size / 2, Size * 3 / 8), (Color4)After);
        spriteBatch.End();

        var pixels = target.GetData<Color>(commandList);
        var bgra = target.Format is PixelFormat.B8G8R8A8_UNorm or PixelFormat.B8G8R8A8_UNorm_SRgb;
        var skia = new Color(Skia.Red, Skia.Green, Skia.Blue, Skia.Alpha);
        var errors = new List<string>();
        void Probe(string what, int x, int y, Color expected)
        {
            var actual = pixels[y * Size + x];
            // GetData hands back the texture's bytes in Stride's RGBA Color, so a BGRA target reads swapped.
            if (bgra)
                actual = new Color(actual.B, actual.G, actual.R, actual.A);
            if (actual != expected)
                errors.Add($"{what} at ({x}, {y}): expected {expected}, got {actual}");
        }

        Probe("before only", Size / 8, Size / 8, Before);
        Probe("Skia over before", Size * 3 / 8, Size / 16, skia);
        Probe("Skia alone, at the top", Size * 5 / 8, Size / 16, skia);
        Probe("after over Skia", Size * 5 / 8, Size * 3 / 16, After);
        Probe("background where a flipped Skia rect would be", Size * 5 / 8, Size * 15 / 16, Background);
        Probe("background", Size / 8, Size * 7 / 8, Background);
        return string.Join("; ", errors);
    }
}
