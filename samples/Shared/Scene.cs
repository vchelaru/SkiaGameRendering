#nullable enable
using System;
using SkiaSharp;
using Svg.Skia;

namespace Sample.Shared;

/// <summary>
/// The one Skia scene every sample draws, imported into every sample project through
/// <c>Scene.props</c> so a human comparing screenshots across backends is comparing the same
/// picture rather than each sample's own one-off drawing code. Each sample keeps its own
/// engine-specific Game/Program - only the actual Skia canvas commands live here.
/// A red circle with an embedded SVG star centered over it, filling the middle half of the canvas.
/// The DesktopGL/WindowsDX <c>--smoke-test</c> checks one pixel on the star and one on the circle
/// outside it, so moving either means updating that check.
/// </summary>
public static class Scene
{
    private static SKSvg? _star;

    public static void Draw(SKCanvas canvas, float width, float height)
    {
        using var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
        float radius = Math.Min(width, height) / 2f;
        canvas.DrawCircle(width / 2f, height / 2f, radius, paint);

        var picture = LoadStar().Picture;
        if (picture == null)
            return;

        float size = radius;
        float scale = size / Math.Max(picture.CullRect.Width, picture.CullRect.Height);
        canvas.Save();
        canvas.Translate((width - size) / 2f, (height - size) / 2f);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    // Embedded rather than loaded from disk so desktop, WASM, and NativeAOT all read it the same way.
    private static SKSvg LoadStar()
    {
        if (_star != null)
            return _star;

        using var stream = typeof(Scene).Assembly.GetManifestResourceStream("Sample.Shared.Star.svg")
            ?? throw new InvalidOperationException("Embedded resource Sample.Shared.Star.svg is missing.");
        var svg = new SKSvg();
        svg.Load(stream);
        return _star = svg;
    }
}
