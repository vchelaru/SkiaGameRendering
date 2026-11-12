using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Shared;

/// <summary>
/// An antialiased triangle drawn on a transparent 4x multisampled target, and the check that the sampled texture holds it
/// resolved: solid inside, empty outside, and edge pixels whose coverage is a whole number of quarters. Analytic (coverage)
/// antialiasing gives arbitrary edge values, and an unresolved image stays empty, so this fails unless 4 samples were
/// drawn and resolved.
/// </summary>
static class MultiSampleChecks
{
    internal static void DrawTriangle(SKCanvas canvas)
    {
        using var path = SKPath.ParseSvgPathData("M8 8 L56 12 L20 56 Z");
        using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = true };
        canvas.DrawPath(path, paint);
    }

    /// <summary><paramref name="rgba"/> is the target read back top row first, <paramref name="size"/> pixels square.</summary>
    internal static void AssertFourSampleTriangle(byte[] rgba, int size, ITestOutputHelper output)
    {
        int At(int x, int y) => (y * size + x) * 4;
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, rgba[At(28, 25)..(At(28, 25) + 4)]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, rgba[At(60, 60)..(At(60, 60) + 4)]);

        int partial = 0, offQuarter = 0;
        for (var y = 4; y < size; y++)
            for (var x = 4; x < size; x++)
            {
                var alpha = rgba[At(x, y) + 3];
                if (alpha is 0 or 255)
                    continue;
                partial++;
                // Premultiplied red over transparent: red equals alpha. Coverage k/4 resolves to 64, 128 or 191.
                Assert.Equal(alpha, rgba[At(x, y)]);
                if (new[] { 64, 128, 191 }.All(q => Math.Abs(alpha - q) > 2))
                    offQuarter++;
            }

        output.WriteLine($"{partial} antialiased edge pixels, {offQuarter} not a whole number of quarters.");
        Assert.True(partial >= 20, $"Only {partial} antialiased edge pixels; the edges were not smoothed.");
        Assert.Equal(0, offQuarter);
    }
}
