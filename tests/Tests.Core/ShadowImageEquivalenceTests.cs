using SkiaSharp;
using Xunit;

namespace Tests.Core;

/// <summary>
/// Backs docs/performance/shadows.md: a blurred shadow rendered once into an image and drawn at an
/// integer position matches the direct mask-filter blur exactly (raster surfaces, no tolerance).
/// </summary>
public class ShadowImageEquivalenceTests
{
    [Theory]
    [InlineData(112, 78, 6f, 4f, 6f, 140)]
    [InlineData(168, 90, 5f, 0f, 4f, 120)]
    [InlineData(80, 50, 5f, 0f, 4f, 120)]
    public void CachedImage_AtIntegerPosition_MatchesDirectBlur(int w, int h, float sigma, float dx, float dy, int alpha)
    {
        int pad = (int)Math.Ceiling(3 * sigma);
        using var mask = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
        using var paint = new SKPaint { IsAntialias = true, MaskFilter = mask, Color = new SKColor(0, 0, 0, (byte)alpha) };

        using var cache = SKSurface.Create(new SKImageInfo(w + 2 * pad, h + 2 * pad));
        cache.Canvas.DrawRoundRect(new SKRect(pad, pad, pad + w, pad + h), 8, 8, paint);
        using var image = cache.Snapshot();

        var info = new SKImageInfo(w + 100, h + 100);
        using var direct = SKSurface.Create(info);
        using var cached = SKSurface.Create(info);
        float x = 20 + dx, y = 20 + dy;
        direct.Canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), 8, 8, paint);
        cached.Canvas.DrawImage(image, x - pad, y - pad, new SKSamplingOptions(SKFilterMode.Linear), null);

        using var a = direct.PeekPixels();
        using var b = cached.PeekPixels();
        var pa = a.GetPixelSpan();
        var pb = b.GetPixelSpan();
        int differing = 0;
        for (int i = 0; i < pa.Length; i++)
            if (pa[i] != pb[i]) differing++;
        Assert.Equal(0, differing);
    }
}
