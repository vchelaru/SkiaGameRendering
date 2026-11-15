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
/// Shapes sit in a 3x3 grid of <see cref="Cell"/>-sized cells, filled left to right then top to bottom: a red circle
/// (cell 0), an embedded SVG water drop (1), a rounded rectangle with a linear gradient (2), the Gum logo, an
/// embedded PNG with transparency (3), then a gold circle cut by an antialiased clip (4, see <see cref="ClipCenter"/>).
/// Add new shapes in the next free cell, from 5 on. The smoke tests of every sample, <c>tests/Tests.Godot</c> and the
/// WebGL Playwright specs check the centers of these cells (and the logo and clip pixels below), so moving a shape
/// means updating those checks. The specs repeat the coordinates, since JavaScript cannot read these constants.
/// </summary>
public static class Scene
{
    public const int Width = 800;
    public const int Height = 800;
    public const int Columns = 3;

    /// <summary>266: three cells fill 798 of the 800 pixels, leaving a 2 px strip at the right and bottom.</summary>
    public const int Cell = Width / Columns;
    public const int CellCenter = Cell / 2;

    public const int CircleCell = 0;
    public const int DropCell = 1;
    public const int GradientCell = 2;
    public const int LogoCell = 3;
    public const int ClipCell = 4;

    /// <summary>The center of cell <paramref name="index"/> in canvas pixels: the circle's is (133, 133), the drop's (399, 133).</summary>
    public static int CellCenterX(int index) => index % Columns * Cell + CellCenter;
    public static int CellCenterY(int index) => index / Columns * Cell + CellCenter;

    /// <summary>A pixel inside the logo's opaque part, and one in the logo cell's transparent top margin.</summary>
    public const int LogoOpaqueX = 130;
    public const int LogoOpaqueY = Cell + CellCenter;
    public const int LogoTransparentX = CellCenter;
    public const int LogoTransparentY = Cell + 40;

    /// <summary>
    /// The clipped circle's center (the middle cell, so the same in x and y), which is gold, and a pixel inside the
    /// circle but outside the clip, which shows the background. Skia ends the frame with the scissor test on for that
    /// clip, so a runtime whose composite inherits Skia's GL state copies only the clip's rectangle. Every other cell's
    /// check pixel lies outside it, so those checks catch that.
    /// </summary>
    public const int ClipCenter = Cell + CellCenter;
    public const int ClippedOutX = ClipCenter;
    public const int ClippedOutY = ClipCenter - 75;
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
        static SKRect CellRect(int index) => SKRect.Create(index % Columns * cell + inset, index / Columns * cell + inset, size, size);

        using var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
        var circle = CellRect(CircleCell);
        canvas.DrawCircle(circle.MidX, circle.MidY, size / 2f, paint);

        var picture = LoadDrop().Picture;
        if (picture != null)
        {
            var drop = CellRect(DropCell);
            canvas.Save();
            canvas.Translate(drop.Left, drop.Top);
            canvas.Scale(size / Math.Max(picture.CullRect.Width, picture.CullRect.Height));
            canvas.DrawPicture(picture);
            canvas.Restore();
        }

        var rect = CellRect(GradientCell);
        using var gradient = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
            new[] { new SKColor(0xF7, 0x25, 0x85), new SKColor(0x72, 0x09, 0xB7) }, SKShaderTileMode.Clamp);
        using var gradientPaint = new SKPaint { Shader = gradient, IsAntialias = true };
        canvas.DrawRoundRect(rect, size * 0.2f, size * 0.2f, gradientPaint);

        // A decoded PNG with transparency, fitted to the cell with its aspect ratio kept.
        var logo = LoadLogo();
        var logoCell = CellRect(LogoCell);
        var fit = Math.Min(logoCell.Width / logo.Width, logoCell.Height / logo.Height);
        var logoRect = SKRect.Create(
            logoCell.MidX - logo.Width * fit / 2f, logoCell.MidY - logo.Height * fit / 2f, logo.Width * fit, logo.Height * fit);
        canvas.DrawImage(logo, logoRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

        // A circle cut to a band by an antialiased rounded-rect clip wider than it. Keep this the last draw: the clip
        // leaves Skia's scissor test on (see ClipCenter).
        canvas.Save();
        using (var clip = new SKRoundRect(SKRect.Create(ClipCenter - 120f, ClipCenter - 50f, 240f, 100f), 16f, 16f))
            canvas.ClipRoundRect(clip, antialias: true);
        using var clipPaint = new SKPaint { Color = ClipColor, IsAntialias = true };
        canvas.DrawCircle(ClipCenter, ClipCenter, size / 2f, clipPaint);
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
