using SkiaGameRendering;
using SkiaSharp;
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.StrideD3D12;

/// <summary>
/// <see cref="SkiaDirectCanvas"/> on a WARP Stride device. Each case draws a Stride sprite, then Skia,
/// then another Stride sprite, then Skia again into whatever is bound, and reads the target back: a flip, a
/// channel swap, Skia landing out of order, or Stride's cached state going stale after a Skia pass each fails a
/// probe. The Stride colors only use 0 and 255 channels, which read back the same through an sRGB view.
/// WARP draws correct pixels even with wrong barriers, so <see cref="StrideD3D12BarrierValidationTests"/> runs
/// the same cycle under the debug layer.
/// </summary>
public sealed class StrideD3D12DirectDrawTests : IDisposable
{
    const int Size = 64;
    const string Golden = "stride-d3d12-scene.png";

    static readonly Color Background = new(0, 0, 255, 255);
    static readonly Color Before = new(0, 255, 0, 255);
    static readonly SKColor Skia = new(210, 50, 90);
    static readonly Color After = new(255, 255, 0, 255);
    static readonly SKColor SkiaLast = new(10, 20, 30);

    readonly GraphicsDevice _graphicsDevice = WarpStrideDevice.Create(ColorSpace.Gamma);

    public void Dispose()
    {
        SkiaRenderer.Dispose();
        _graphicsDevice.Dispose();
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
            var errors = DrawAndCheck(presenter.BackBuffer);
            Assert.True(errors == "", errors);
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
        using var target = Texture.New2D(_graphicsDevice, Size, Size, format,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        var errors = DrawAndCheck(target);
        Assert.True(errors == "", errors);
    }

    /// <summary>Skia would encode its writes into an sRGB-typed texture, and Stride's HDR targets aren't 8-bit.</summary>
    [Theory]
    [InlineData(PixelFormat.R8G8B8A8_UNorm_SRgb)]
    [InlineData(PixelFormat.R16G16B16A16_Float)]
    public void UnsupportedFormat_ThrowsNotSupported_AndCanStillDraw(PixelFormat format)
    {
        using var target = Texture.New2D(_graphicsDevice, Size, Size, format,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        using var supported = Texture.New2D(_graphicsDevice, Size, Size, PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        using var commandList = CommandList.New(_graphicsDevice);
        var graphicsContext = new GraphicsContext(_graphicsDevice, commandList: commandList);
        commandList.SetRenderTargetAndViewport(null, target);

        using var direct = new SkiaDirectCanvas(_graphicsDevice);
        Assert.Throws<NotSupportedException>(() => direct.Begin(graphicsContext));

        // A refused Begin leaves no pass open, and left the command list as it was.
        Assert.Throws<InvalidOperationException>(() => direct.End());
        commandList.SetRenderTargetAndViewport(null, supported);
        direct.Begin(graphicsContext);
        direct.End();
        _graphicsDevice.ExecuteCommandList(commandList.Close());
    }

    /// <summary>A multisampled target would need a resolve; Skia is not asked to guess.</summary>
    [Fact]
    public void MultisampledTarget_ThrowsNotSupported()
    {
        var description = TextureDescription.New2D(Size, Size, PixelFormat.R8G8B8A8_UNorm, TextureFlags.RenderTarget);
        description.MultisampleCount = MultisampleCount.X4;
        using var target = Texture.New(_graphicsDevice, description);
        using var commandList = CommandList.New(_graphicsDevice);
        var graphicsContext = new GraphicsContext(_graphicsDevice, commandList: commandList);
        commandList.SetRenderTargetAndViewport(null, target);

        using var direct = new SkiaDirectCanvas(_graphicsDevice);
        Assert.Throws<NotSupportedException>(() => direct.Begin(graphicsContext));
        _graphicsDevice.ExecuteCommandList(commandList.Close());
    }

    [Fact]
    public void NothingBound_Throws()
    {
        using var commandList = CommandList.New(_graphicsDevice);
        var graphicsContext = new GraphicsContext(_graphicsDevice, commandList: commandList);
        commandList.SetRenderTargets(null, ReadOnlySpan<Texture>.Empty);
        using var direct = new SkiaDirectCanvas(_graphicsDevice);
        Assert.Throws<InvalidOperationException>(() => direct.Begin(graphicsContext));
        _graphicsDevice.ExecuteCommandList(commandList.Close());
    }

    [Fact]
    public void BeginTwice_AndEndWithoutBegin_Throw()
    {
        using var target = Texture.New2D(_graphicsDevice, Size, Size, PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        using var commandList = CommandList.New(_graphicsDevice);
        var graphicsContext = new GraphicsContext(_graphicsDevice, commandList: commandList);
        commandList.SetRenderTargetAndViewport(null, target);

        using var direct = new SkiaDirectCanvas(_graphicsDevice);
        Assert.Throws<InvalidOperationException>(() => direct.End());
        Assert.Throws<InvalidOperationException>(() => direct.Canvas);
        direct.Begin(graphicsContext);
        Assert.Throws<InvalidOperationException>(() => direct.Begin(graphicsContext));
        Assert.Throws<InvalidOperationException>(() => direct.Dispose());
        direct.End();
        _graphicsDevice.ExecuteCommandList(commandList.Close());
    }

    [Fact]
    public void Scene_DrawnToRenderTarget_MatchesGolden() =>
        GoldenImage.AssertMatchesGolden(StrideSkiaGolden.RenderSceneDirect(_graphicsDevice), Golden);

    /// <summary>
    /// Draws the layers into <paramref name="target"/> and reports every probe that is wrong, or "". Several
    /// Skia passes in one command list: the second reuses whatever the first left behind, and the one after
    /// Stride's last sprite has to find the target where Stride's earlier submit left it.
    /// </summary>
    string DrawAndCheck(Texture target)
    {
        using var white = Texture.New2D(_graphicsDevice, 1, 1, PixelFormat.R8G8B8A8_UNorm, new[] { Color.White });
        using var spriteBatch = new SpriteBatch(_graphicsDevice);
        using var direct = new SkiaDirectCanvas(_graphicsDevice);
        using var paint = new SKPaint { Color = Skia };
        using var lastPaint = new SKPaint { Color = SkiaLast };

        using var commandList = CommandList.New(_graphicsDevice);
        var graphicsContext = new GraphicsContext(_graphicsDevice, commandList: commandList);
        commandList.SetRenderTargetAndViewport(null, target);
        commandList.Clear(target, (Color4)Background);

        // Before: the top-left quadrant.
        commandList.ResourceBarrierTransition(white, BarrierLayout.ShaderResource);
        spriteBatch.Begin(graphicsContext);
        spriteBatch.Draw(white, new RectangleF(0, 0, Size / 2, Size / 2), (Color4)Before);
        spriteBatch.End();

        // Skia: a strip along the top, across the middle half. Twice, so the second pass starts from what
        // the first handed back.
        for (var i = 0; i < 2; i++)
        {
            direct.Begin(graphicsContext);
            direct.Canvas.DrawRect(Size / 4, 0, Size / 2, Size / 4, paint);
            direct.End();
        }

        // After: the right half, from 1/8 down to 1/2. Stride has to find its target again after Skia.
        spriteBatch.Begin(graphicsContext);
        spriteBatch.Draw(white, new RectangleF(Size / 2, Size / 8, Size / 2, Size * 3 / 8), (Color4)After);
        spriteBatch.End();

        // Skia once more, over the sprite just drawn: a square straddling the middle of its right edge.
        direct.Begin(graphicsContext);
        direct.Canvas.DrawRect(Size * 3 / 4, Size / 4, Size / 4, Size / 4, lastPaint);
        direct.End();

        var pixels = target.GetData<Color>(commandList);
        _graphicsDevice.ExecuteCommandList(commandList.Close());
        var bgra = target.Format is PixelFormat.B8G8R8A8_UNorm or PixelFormat.B8G8R8A8_UNorm_SRgb;
        var skia = new Color(Skia.Red, Skia.Green, Skia.Blue, Skia.Alpha);
        var skiaLast = new Color(SkiaLast.Red, SkiaLast.Green, SkiaLast.Blue, SkiaLast.Alpha);
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
        Probe("Skia over after", Size * 7 / 8, Size * 5 / 16, skiaLast);
        Probe("after beside the last Skia", Size * 5 / 8, Size * 5 / 16, After);
        Probe("background where a flipped Skia rect would be", Size * 5 / 8, Size * 15 / 16, Background);
        Probe("background", Size / 8, Size * 7 / 8, Background);
        return string.Join("; ", errors);
    }
}
