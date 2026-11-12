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
/// The canvas is always <see cref="Width"/> x <see cref="Height"/> (800x800): no sample picks its own
/// size, so the layout is identical everywhere, and a window of any size shows it 1:1 or scaled to fit.
/// Shapes sit in a 2x2 grid of <see cref="Cell"/>-sized cells, filled left to right then top to bottom: a red circle
/// (cell 0), an embedded SVG water drop (1), a rounded rectangle with a linear gradient (2), then the
/// Gum logo, an embedded PNG with transparency (3). Last, in the gap where the four cells meet, a gold circle cut
/// to a rounded rectangle by an antialiased clip (see <see cref="ClipCenter"/>): Skia ends the frame with the
/// scissor test on for that clip, so an engine that inherits Skia's GL state composites only that rectangle and fails
/// the cell checks. Add new shapes before it, in the next free cell. The smoke tests of every
/// sample and <c>tests/Tests.Godot</c> check the centers of these cells (and two logo pixels, see
/// <see cref="LogoOpaqueX"/>), so moving a shape means updating those checks.
/// </summary>
public static class Scene
{
    public const int Width = 800;
    public const int Height = 800;
    public const int Cell = 400;

    /// <summary>Cell centers in canvas pixels: circle (100,100 in a 200 cell grid) is CellCenter, CellCenter and so on.</summary>
    public const int CellCenter = Cell / 2;

    /// <summary>A pixel inside the logo's opaque part, and one in the logo cell's transparent top margin.</summary>
    public const int LogoOpaqueX = 595;
    public const int LogoOpaqueY = 600;
    public const int LogoTransparentX = Cell + CellCenter;
    public const int LogoTransparentY = Cell + 60;

    /// <summary>
    /// The clipped circle's center, which is gold, and a pixel inside the circle but outside the clip, which shows the
    /// background.
    /// </summary>
    public const int ClipCenter = Cell;
    public const int ClippedOutX = Cell;
    public const int ClippedOutY = Cell - 30;
    public static readonly SKColor ClipColor = new(255, 200, 0);

    /// <summary>
    /// The solid color every sample's window or back buffer is cleared to behind the Scene (cornflower blue), so
    /// screenshots of different runtimes compare. The Scene itself leaves it transparent.
    /// </summary>
    public static readonly SKColor Background = new(100, 149, 237);

    private static SKSvg? _drop;
    private static SKImage? _logo;

    public static void Draw(SKCanvas canvas)
    {
        const float cell = Cell;
        const float inset = cell * 0.1f;
        const float size = cell - 2 * inset;
        const int columns = Width / Cell;
        static SKRect CellRect(int index) => SKRect.Create(index % columns * cell + inset, index / columns * cell + inset, size, size);

        using var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawCircle(cell / 2f, cell / 2f, cell / 2f - inset, paint);

        var picture = LoadDrop().Picture;
        if (picture != null)
        {
            var drop = CellRect(1);
            canvas.Save();
            canvas.Translate(drop.Left, drop.Top);
            canvas.Scale(size / Math.Max(picture.CullRect.Width, picture.CullRect.Height));
            canvas.DrawPicture(picture);
            canvas.Restore();
        }

        var rect = CellRect(2);
        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
            new[] { new SKColor(0xF7, 0x25, 0x85), new SKColor(0x72, 0x09, 0xB7) }, SKShaderTileMode.Clamp);
        using var gradientPaint = new SKPaint { Shader = gradient, IsAntialias = true };
        canvas.DrawRoundRect(rect, size * 0.2f, size * 0.2f, gradientPaint);

        // A decoded PNG with transparency, fitted to the cell with its aspect ratio kept.
        var logo = LoadLogo();
        var logoCell = CellRect(3);
        var fit = Math.Min(logoCell.Width / logo.Width, logoCell.Height / logo.Height);
        var logoRect = SKRect.Create(
            logoCell.MidX - logo.Width * fit / 2f, logoCell.MidY - logo.Height * fit / 2f, logo.Width * fit, logo.Height * fit);
        canvas.DrawImage(logo, logoRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        // Keep this the last draw: it leaves Skia's scissor test on (see the class summary).
        canvas.Save();
        using (var clip = new SKRoundRect(SKRect.Create(ClipCenter - 24f, ClipCenter - 14f, 48f, 28f), 8f, 8f))
            canvas.ClipRoundRect(clip, antialias: true);
        using var clipPaint = new SKPaint { Color = ClipColor, IsAntialias = true };
        canvas.DrawCircle(ClipCenter, ClipCenter, 36f, clipPaint);
        canvas.Restore();
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

    private static SKImage LoadLogo()
    {
        if (_logo != null)
            return _logo;

        using var stream = typeof(Scene).Assembly.GetManifestResourceStream("SampleShared.Gum.png")
            ?? throw new InvalidOperationException("Embedded resource SampleShared.Gum.png is missing.");
        return _logo = SKImage.FromEncodedData(stream)
            ?? throw new InvalidOperationException("Could not decode the embedded Gum.png.");
    }
}
