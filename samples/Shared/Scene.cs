#nullable enable
using System;
using SkiaSharp;
using Svg.Skia;

namespace SampleShared;

/// <summary>
/// The one Skia scene every sample draws, imported into every sample project through
/// <c>Scene.props</c> so a human comparing screenshots across backends is comparing the same
/// picture rather than each sample's own one-off drawing code. Each sample keeps its own
/// engine-specific Game/Program - only the actual Skia canvas commands live here.
/// Shapes sit in a grid of square cells, half the canvas's shorter side each, filled left to
/// right then top to bottom, as many columns as fit across: a red circle, an embedded SVG water drop,
/// then a rounded rectangle with a linear gradient. Add new shapes in the next free cell. The DesktopGL/WindowsDX/Unity <c>--smoke-test</c> and <c>tests/Tests.Godot</c> check
/// the center of the first two cells, so moving either shape means updating those checks.
/// </summary>
public static class Scene
{
    private static SKSvg? _drop;

    public static void Draw(SKCanvas canvas, float width, float height)
    {
        float cell = Math.Min(width, height) / 2f;
        float inset = cell * 0.1f;

        int columns = Math.Max(1, (int)(width / cell));
        float size = cell - 2 * inset;
        SKRect Cell(int index) => SKRect.Create(index % columns * cell + inset, index / columns * cell + inset, size, size);

        using var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(cell / 2f, cell / 2f, cell / 2f - inset, paint);

        var picture = LoadDrop().Picture;
        if (picture != null)
        {
            var drop = Cell(1);
            canvas.Save();
            canvas.Translate(drop.Left, drop.Top);
            canvas.Scale(size / Math.Max(picture.CullRect.Width, picture.CullRect.Height));
            canvas.DrawPicture(picture);
            canvas.Restore();
        }

        var rect = Cell(2);
        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
            new[] { new SKColor(0xF7, 0x25, 0x85), new SKColor(0x72, 0x09, 0xB7) }, SKShaderTileMode.Clamp);
        using var gradientPaint = new SKPaint { Shader = gradient, IsAntialias = true };
        canvas.DrawRoundRect(rect, size * 0.2f, size * 0.2f, gradientPaint);
    }

    // Embedded rather than loaded from disk so desktop, WASM, and NativeAOT all read it the same way.
    private static SKSvg LoadDrop()
    {
        if (_drop != null)
            return _drop;

        using var stream = typeof(Scene).Assembly.GetManifestResourceStream("SampleShared.Drop.svg")
            ?? throw new InvalidOperationException("Embedded resource SampleShared.Drop.svg is missing.");
        var svg = new SKSvg();
        svg.Load(stream);
        return _drop = svg;
    }
}
