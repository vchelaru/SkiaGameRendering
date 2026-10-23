using SkiaSharp;

namespace Benchmarks.DirectDraw;

/// <summary>
/// A fixed, seeded scene of a few hundred Skia draws: filled star paths, gradient rounded rects, text and
/// stroked curves. Both modes draw exactly this, shifted a little each frame so nothing is static.
/// </summary>
sealed class BenchmarkScene : IDisposable
{
    const int Stars = 200;
    const int Cards = 100;
    const int Labels = 100;
    const int Curves = 50;

    internal const int DrawCount = Stars + Cards + Labels + Curves;

    readonly List<(SKPath Path, SKPaint Paint)> _stars = new();
    readonly List<(SKRect Rect, SKPaint Paint)> _cards = new();
    readonly List<(SKPoint At, string Text, SKPaint Paint)> _labels = new();
    readonly List<(SKPath Path, SKPaint Paint)> _curves = new();
    readonly SKFont _font = new(SKTypeface.Default, 18);

    internal BenchmarkScene(int width, int height)
    {
        var random = new Random(1234);
        SKColor RandomColor(byte alpha) => new((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha);

        for (var i = 0; i < Stars; i++)
        {
            using var builder = new SKPathBuilder();
            var center = new SKPoint(random.Next(width), random.Next(height));
            var outer = 10f + random.Next(40);
            for (var p = 0; p < 10; p++)
            {
                var radius = p % 2 == 0 ? outer : outer * 0.45f;
                var angle = p * MathF.PI / 5f;
                var point = new SKPoint(center.X + radius * MathF.Sin(angle), center.Y - radius * MathF.Cos(angle));
                if (p == 0) builder.MoveTo(point); else builder.LineTo(point);
            }
            builder.Close();
            _stars.Add((builder.Detach(), new SKPaint { Color = RandomColor(200), IsAntialias = true }));
        }

        for (var i = 0; i < Cards; i++)
        {
            var rect = SKRect.Create(random.Next(width), random.Next(height), 40 + random.Next(160), 20 + random.Next(100));
            var shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                new[] { RandomColor(220), RandomColor(220) }, SKShaderTileMode.Clamp);
            _cards.Add((rect, new SKPaint { Shader = shader, IsAntialias = true }));
        }

        for (var i = 0; i < Labels; i++)
            _labels.Add((new SKPoint(random.Next(width), random.Next(height)), $"Label {i} quick brown fox",
                new SKPaint { Color = RandomColor(255), IsAntialias = true }));

        for (var i = 0; i < Curves; i++)
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(random.Next(width), random.Next(height));
            builder.CubicTo(random.Next(width), random.Next(height), random.Next(width), random.Next(height),
                random.Next(width), random.Next(height));
            _curves.Add((builder.Detach(), new SKPaint
            {
                Color = RandomColor(255), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 + random.Next(6),
            }));
        }
    }

    internal void Draw(SKCanvas canvas, int frame)
    {
        canvas.Save();
        canvas.Translate(frame % 32, (frame / 2) % 16);

        foreach (var (rect, paint) in _cards)
            canvas.DrawRoundRect(rect, 8, 8, paint);
        foreach (var (path, paint) in _stars)
            canvas.DrawPath(path, paint);
        foreach (var (path, paint) in _curves)
            canvas.DrawPath(path, paint);
        foreach (var (at, text, paint) in _labels)
            canvas.DrawText(text, at.X, at.Y, SKTextAlign.Left, _font, paint);

        canvas.Restore();
    }

    public void Dispose()
    {
        foreach (var (path, paint) in _stars) { path.Dispose(); paint.Dispose(); }
        foreach (var (_, paint) in _cards) { paint.Shader?.Dispose(); paint.Dispose(); }
        foreach (var (_, _, paint) in _labels) paint.Dispose();
        foreach (var (path, paint) in _curves) { path.Dispose(); paint.Dispose(); }
        _font.Dispose();
    }
}
