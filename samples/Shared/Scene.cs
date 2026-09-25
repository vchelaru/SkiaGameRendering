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
/// Shapes sit in a grid of square cells, half the canvas's shorter side each, filled left to
/// right then top to bottom: a red circle, then an embedded SVG water drop. Add new shapes in the
/// next free cell. The DesktopGL/WindowsDX <c>--smoke-test</c> and <c>tests/Tests.Godot</c> check
/// the center of the first two cells, so moving either shape means updating those checks.
/// </summary>
public static class Scene
{
    private static SKSvg? _drop;

    public static void Draw(SKCanvas canvas, float width, float height)
    {
        float cell = Math.Min(width, height) / 2f;
        float inset = cell * 0.1f;

        using var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(cell / 2f, cell / 2f, cell / 2f - inset, paint);

        var picture = LoadDrop().Picture;
        if (picture == null)
            return;

        float size = cell - 2 * inset;
        canvas.Save();
        canvas.Translate(cell + inset, inset);
        canvas.Scale(size / Math.Max(picture.CullRect.Width, picture.CullRect.Height));
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    // Embedded rather than loaded from disk so desktop, WASM, and NativeAOT all read it the same way.
    private static SKSvg LoadDrop()
    {
        if (_drop != null)
            return _drop;

        using var stream = typeof(Scene).Assembly.GetManifestResourceStream("Sample.Shared.Drop.svg")
            ?? throw new InvalidOperationException("Embedded resource Sample.Shared.Drop.svg is missing.");
        var svg = new SKSvg();
        svg.Load(stream);
        return _drop = svg;
    }
}
