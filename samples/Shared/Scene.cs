using System;
using SkiaSharp;

namespace Sample.Shared;

/// <summary>
/// The one Skia scene every sample draws, linked into every sample project (see each
/// <c>Compile Include="..\Shared\Scene.cs"</c>) so a human comparing screenshots across backends is
/// comparing the same picture rather than each sample's own one-off drawing code. Each sample keeps
/// its own engine-specific Game/Program - only the actual Skia canvas commands live here.
/// Deliberately just a circle for now; extend this method (not a per-sample copy of it) when a
/// sample needs to prove something a circle can't.
/// </summary>
public static class Scene
{
    public static void Draw(SKCanvas canvas, float width, float height)
    {
        using var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
        float radius = Math.Min(width, height) / 2f;
        canvas.DrawCircle(width / 2f, height / 2f, radius, paint);
    }
}
