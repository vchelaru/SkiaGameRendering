using Apos.Shapes;
using Microsoft.Xna.Framework;
using SkiaSharp;

namespace Benchmarks.ShapeRendering
{
    /// <summary>
    /// Draws a <see cref="Scene"/> with either backend. Both methods walk the same
    /// <see cref="ShapeInstance"/> array and evaluate the same animation formulas, so the only
    /// thing that differs frame-to-frame is which drawing API is issuing the calls.
    /// </summary>
    internal static class ShapeRenderers
    {
        public static void DrawSceneSkia(SKCanvas canvas, Scene scene, float t, SKPaint fill, SKPaint stroke, SKPathBuilder scratchPath)
        {
            var shapes = scene.Shapes;
            for (int i = 0; i < shapes.Length; i++)
            {
                ref readonly var s = ref shapes[i];
                var pos = scene.AnimatedPosition(i, t);
                fill.Color = ToSk(s.FillColor);
                fill.Shader = null;

                switch (s.Kind)
                {
                    case ShapeKind.Circle:
                        canvas.DrawCircle(pos.X, pos.Y, s.Size, fill);
                        break;

                    case ShapeKind.Gradient:
                    {
                        // Same two colors used natively by Apos.Shapes' Gradient struct on the other
                        // side, so both renderers are filling with an equivalent linear gradient.
                        float rotation = scene.AnimatedRotation(i, t);
                        var dir = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
                        var p0 = pos - dir * s.Size;
                        var p1 = pos + dir * s.Size;
                        using var shader = SKShader.CreateLinearGradient(
                            new SKPoint(p0.X, p0.Y), new SKPoint(p1.X, p1.Y),
                            new[] { ToSk(s.FillColor), ToSk(s.BorderColor) }, SKShaderTileMode.Clamp);
                        fill.Shader = shader;
                        canvas.DrawCircle(pos.X, pos.Y, s.Size, fill);
                        break;
                    }

                    case ShapeKind.RoundedRect:
                    {
                        float rotation = scene.AnimatedRotation(i, t);
                        float half = s.Size;
                        var rect = new SKRect(-half, -half, half, half);
                        float corner = half * 0.3f;

                        canvas.Save();
                        canvas.Translate(pos.X, pos.Y);
                        canvas.RotateDegrees(rotation * (180f / MathF.PI));
                        canvas.DrawRoundRect(rect, corner, corner, fill);
                        stroke.Color = ToSk(s.BorderColor);
                        stroke.StrokeWidth = s.Thickness;
                        canvas.DrawRoundRect(rect, corner, corner, stroke);
                        canvas.Restore();
                        break;
                    }

                    case ShapeKind.Line:
                    {
                        var p2 = pos + s.LineOffset;
                        stroke.Color = fill.Color;
                        stroke.StrokeWidth = s.Size * 2f;
                        stroke.StrokeCap = SKStrokeCap.Round;
                        canvas.DrawLine(pos.X, pos.Y, p2.X, p2.Y, stroke);
                        break;
                    }

                    case ShapeKind.Triangle:
                    {
                        float rotation = scene.AnimatedRotation(i, t);
                        BuildEquilateralTriangle(scratchPath, pos, s.Size, rotation);
                        using (var path = scratchPath.Detach())
                            canvas.DrawPath(path, fill);
                        break;
                    }
                }
            }
        }

        public static void DrawSceneApos(ShapeBatch shapeBatch, Scene scene, float t)
        {
            var shapes = scene.Shapes;
            for (int i = 0; i < shapes.Length; i++)
            {
                ref readonly var s = ref shapes[i];
                var pos = scene.AnimatedPosition(i, t);

                switch (s.Kind)
                {
                    case ShapeKind.Circle:
                        shapeBatch.FillCircle(pos, s.Size, s.FillColor);
                        break;

                    case ShapeKind.Gradient:
                    {
                        float rotation = scene.AnimatedRotation(i, t);
                        var dir = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
                        var p0 = pos - dir * s.Size;
                        var p1 = pos + dir * s.Size;
                        var gradient = new Gradient(p0, s.FillColor, p1, s.BorderColor);
                        shapeBatch.FillCircle(pos, s.Size, gradient);
                        break;
                    }

                    case ShapeKind.RoundedRect:
                    {
                        float rotation = scene.AnimatedRotation(i, t);
                        var size = new Vector2(s.Size * 2f, s.Size * 2f);
                        var xy = pos - size / 2f;
                        float corner = s.Size * 0.3f;
                        shapeBatch.FillRectangle(xy, size, s.FillColor, corner, rotation);
                        shapeBatch.BorderRectangle(xy, size, s.BorderColor, s.Thickness, corner, rotation);
                        break;
                    }

                    case ShapeKind.Line:
                    {
                        var p2 = pos + s.LineOffset;
                        shapeBatch.FillLine(pos, p2, s.Size, s.FillColor);
                        break;
                    }

                    case ShapeKind.Triangle:
                    {
                        float rotation = scene.AnimatedRotation(i, t);
                        shapeBatch.FillEquilateralTriangle(pos, s.Size, s.FillColor, rotation: rotation);
                        break;
                    }
                }
            }
        }

        private static void BuildEquilateralTriangle(SKPathBuilder path, Vector2 center, float radius, float rotation)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                float angle = rotation + corner * (MathF.PI * 2f / 3f) - MathF.PI / 2f;
                float x = center.X + MathF.Cos(angle) * radius;
                float y = center.Y + MathF.Sin(angle) * radius;
                if (corner == 0)
                    path.MoveTo(x, y);
                else
                    path.LineTo(x, y);
            }
            path.Close();
        }

        private static SKColor ToSk(Color c) => new SKColor(c.R, c.G, c.B, c.A);
    }
}
