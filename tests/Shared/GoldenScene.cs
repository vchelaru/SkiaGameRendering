using SkiaSharp;

namespace Tests.Shared;

/// <summary>
/// The one fixed scene every backend's golden-image test draws. Deliberately asymmetric in both
/// axes so a flipped or transposed readback can't match the golden by accident, and deliberately
/// free of text - font rasterization varies with the installed font stack, which would make the
/// goldens machine-dependent for reasons that have nothing to do with this library.
/// <para>
/// Each element is here to catch a different class of breakage: the hard-edged rect catches
/// structural faults (wrong color channel order, wrong row pitch, wrong origin) with no
/// antialiasing to blur the evidence; the antialiased circle and stroked path catch a Skia GPU
/// pipeline that silently fell back to a different rasterization path; the gradient catches shader
/// setup; the translucent overlap catches a blend state that the engine's own state leaked into.
/// </para>
/// </summary>
static class GoldenScene
{
    /// <summary>Every golden is rendered at this size. Small enough to keep the PNGs a few KB.</summary>
    internal const int Width = 128;
    internal const int Height = 128;

    /// <summary>Background. Opaque and dark, so an alpha-handling bug shows up as a visible shift.</summary>
    internal static readonly SKColor Background = new(18, 22, 30, 255);

    /// <summary>
    /// The hard-edged rect's fill, and the rect itself. Checked directly by
    /// <see cref="GoldenImage"/>'s structural assertions as well as by the golden comparison, so a
    /// regenerated-but-wrong golden still can't hide an upside-down image.
    /// </summary>
    internal static readonly SKColor OpaqueRectColor = new(220, 60, 40, 255);
    internal static readonly SKRect OpaqueRect = SKRect.Create(8, 8, 44, 22);

    internal static void Draw(SKCanvas canvas)
    {
        canvas.Clear(Background);

        using (var paint = new SKPaint { Color = OpaqueRectColor, IsAntialias = false })
            canvas.DrawRect(OpaqueRect, paint);

        using (var paint = new SKPaint { Color = new SKColor(60, 180, 220, 255), IsAntialias = true })
            canvas.DrawCircle(88f, 38f, 26f, paint);

        using (var shader = SKShader.CreateLinearGradient(
                   new SKPoint(10f, 74f), new SKPoint(118f, 74f),
                   [new SKColor(250, 200, 40, 255), new SKColor(140, 40, 200, 255)],
                   SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
            canvas.DrawRect(SKRect.Create(10, 66, 108, 16), paint);

        using (var path = new SKPath())
        using (var paint = new SKPaint
               {
                   Style = SKPaintStyle.Stroke,
                   StrokeWidth = 5f,
                   StrokeCap = SKStrokeCap.Round,
                   Color = new SKColor(120, 230, 140, 255),
                   IsAntialias = true,
               })
        {
            path.MoveTo(14f, 116f);
            path.CubicTo(44f, 88f, 74f, 122f, 116f, 94f);
            canvas.DrawPath(path, paint);
        }

        // Two translucent rects overlapping each other and the shapes above - the overlap region is
        // the part that only comes out right if source-over blending is actually in effect.
        using (var paint = new SKPaint { Color = new SKColor(255, 255, 255, 90), IsAntialias = true })
            canvas.DrawRect(SKRect.Create(30, 20, 40, 40), paint);
        using (var paint = new SKPaint { Color = new SKColor(40, 90, 255, 110), IsAntialias = true })
            canvas.DrawRect(SKRect.Create(52, 44, 46, 34), paint);
    }
}
