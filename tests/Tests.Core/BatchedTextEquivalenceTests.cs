using SkiaSharp;
using Xunit;

namespace Tests.Core;

/// <summary>
/// Pins the claim in docs/performance/text.md: many labels drawn as one positioned <see cref="SKTextBlob"/>
/// come out pixel-identical to one <c>DrawText</c> call per label.
/// </summary>
public sealed class BatchedTextEquivalenceTests
{
    [Fact]
    public void OnePositionedBlob_MatchesDrawTextPerLabel()
    {
        var labels = Enumerable.Range(0, 60).Select(i => $"Score {i * 37 % 100000:00000} Lives {i % 9}").ToArray();
        var origins = labels.Select((_, i) => new SKPoint(8.37f + i % 3 * 190.61f, 20.2f + i / 3 * 22.45f)).ToArray();

        using var typeface = SKTypeface.Default;
        using var font = new SKFont(typeface, 20) { Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };

        byte[] perLabel = Render(canvas =>
        {
            for (int i = 0; i < labels.Length; i++)
                canvas.DrawText(labels[i], origins[i].X, origins[i].Y, SKTextAlign.Left, font, paint);
        });

        byte[] batched = Render(canvas =>
        {
            var glyphs = labels.Select(l => font.GetGlyphs(l)).ToArray();
            int total = glyphs.Sum(g => g.Length);
            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocatePositionedRun(font, total);
            int n = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                var widths = new float[glyphs[i].Length];
                font.GetGlyphWidths(glyphs[i], widths, Span<SKRect>.Empty);
                float x = 0;
                for (int k = 0; k < glyphs[i].Length; k++)
                {
                    run.Glyphs[n] = glyphs[i][k];
                    run.Positions[n] = new SKPoint(origins[i].X + x, origins[i].Y);
                    x += widths[k];
                    n++;
                }
            }
            using var blob = builder.Build();
            canvas.DrawText(blob, 0, 0, paint);
        });

        Assert.Equal(perLabel, batched);
    }

    private static byte[] Render(Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(640, 420, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Black);
        draw(surface.Canvas);
        using var pixmap = surface.PeekPixels();
        return pixmap.GetPixelSpan().ToArray();
    }
}
