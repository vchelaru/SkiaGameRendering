using SkiaSharp;

namespace Performance
{
    // The guide scenes (see PerfScene.AddGuideScenes). Within a set every variant draws the same items, so the only
    // difference between two rows is the one technique the variant names.
    public sealed partial class SkiaSceneDrawer
    {
        private const int SamplingImageSize = 256;
        private const float GuideShadowSigma = 5f;
        private const float GuideShadowDy = 4f;
        private static readonly SKColor GuideShadowColor = new(0, 0, 0, 120);
        private static readonly SKColor GuidePanelColor = new(38, 42, 54);

        private readonly SKPaint _layerPaint = new();
        private SKPath[] _hexagons = Array.Empty<SKPath>();
        private SKPicture? _staticShapes;
        private PerfScene? _staticShapesScene;
        private SKPath[] _stars = Array.Empty<SKPath>();
        private string[] _shortLabels = Array.Empty<string>();
        private SKImageFilter? _dropShadow;
        private SKImage? _shadowImage;
        private SKLattice _shadowLattice;
        private float _shadowMargin;
        private SKImage? _samplingImage;
        private SKRect[] _samplingSource = Array.Empty<SKRect>();
        private SKRotationScaleMatrix[] _samplingTransforms = Array.Empty<SKRotationScaleMatrix>();

        /// <summary>Drops the guide scenes' GPU images when the Skia context changes.</summary>
        private void ResetGuideImages()
        {
            _shadowImage?.Dispose();
            _shadowImage = null;
            _samplingImage?.Dispose();
            _samplingImage = null;
        }

        private void PrepareGuide(GRContext context, PerfScene scene)
        {
            switch (scene.Kind)
            {
                case SceneKind.GuideClip when _hexagons.Length != scene.Count:
                    foreach (var hexagon in _hexagons)
                        hexagon.Dispose();
                    _hexagons = Array.ConvertAll(scene.Items, item => Polygon(6, item.Size * 0.85f, item.Size * 0.85f));
                    break;

                case SceneKind.GuidePicture when !ReferenceEquals(_staticShapesScene, scene):
                {
                    _staticShapes?.Dispose();
                    using var recorder = new SKPictureRecorder();
                    var recording = recorder.BeginRecording(new SKRect(0, 0, scene.Width, scene.Height));
                    DrawAxisAlignedShapes(recording, scene, 0f);
                    _staticShapes = recorder.EndRecording();
                    _staticShapesScene = scene;
                    break;
                }

                case SceneKind.GuideDrawOrder when _shortLabels.Length != scene.Count:
                    _shortLabels = new string[scene.Count];
                    for (int i = 0; i < scene.Count; i++)
                        _shortLabels[i] = $"Item {i}";
                    Font(12);
                    break;

                case SceneKind.GuideFontReuse:
                    Font(PerfScene.SingleFontSize);
                    break;

                case SceneKind.GuidePathReuse when _stars.Length != scene.Count:
                    foreach (var star in _stars)
                        star.Dispose();
                    _stars = Array.ConvertAll(scene.Items, item => Star(item.Size));
                    break;

                case SceneKind.GuideShadow:
                    _dropShadow ??= SKImageFilter.CreateDropShadow(0f, GuideShadowDy, GuideShadowSigma, GuideShadowSigma, GuideShadowColor);
                    if (_shadowImage == null)
                        _shadowImage = BuildShadowImage(context);
                    break;

                case SceneKind.GuideSampling:
                    if (_samplingImage == null)
                    {
                        using var raster = BuildSamplingImage();
                        _samplingImage = raster.ToTextureImage(context, mipmapped: true)
                            ?? throw new InvalidOperationException("SKImage.ToTextureImage failed.");
                    }
                    if (_samplingTransforms.Length != scene.Count)
                    {
                        _samplingTransforms = new SKRotationScaleMatrix[scene.Count];
                        _samplingSource = new SKRect[scene.Count];
                        Array.Fill(_samplingSource, new SKRect(0, 0, SamplingImageSize, SamplingImageSize));
                    }
                    break;
            }
        }

        private void DrawGuide(SKCanvas canvas, PerfScene scene, float t)
        {
            switch (scene.Kind)
            {
                case SceneKind.GuideClip: DrawGuideClip(canvas, scene, t); break;
                case SceneKind.GuidePicture:
                    if (scene.Variant == GuideVariant.PictureReplay)
                        canvas.DrawPicture(_staticShapes!);
                    else
                        DrawAxisAlignedShapes(canvas, scene, 0f);
                    break;
                case SceneKind.GuideItemAlpha: DrawGuideItemAlpha(canvas, scene, t); break;
                case SceneKind.GuideSceneLayer:
                    if (scene.Variant == GuideVariant.SaveLayer)
                    {
                        _layerPaint.Color = new SKColor(0, 0, 0, 200);
                        canvas.SaveLayer(_layerPaint);
                    }
                    DrawCircles(canvas, scene, t);
                    if (scene.Variant == GuideVariant.SaveLayer)
                        canvas.Restore();
                    break;
                case SceneKind.GuideTargetSize:
                    canvas.Save();
                    canvas.Scale(scene.TargetWidth / (float)scene.Width, scene.TargetHeight / (float)scene.Height);
                    DrawAxisAlignedShapes(canvas, scene, t);
                    canvas.Restore();
                    break;
                case SceneKind.GuideDrawOrder: DrawGuideDrawOrder(canvas, scene, t); break;
                case SceneKind.GuidePaintReuse: DrawGuidePaintReuse(canvas, scene, t); break;
                case SceneKind.GuideFontReuse: DrawGuideFontReuse(canvas, scene, t); break;
                case SceneKind.GuidePathReuse: DrawGuidePathReuse(canvas, scene, t); break;
                case SceneKind.GuideShadow: DrawGuideShadow(canvas, scene, t); break;
                case SceneKind.GuideStroke: DrawGuideStroke(canvas, scene, t); break;
                case SceneKind.GuideSampling: DrawGuideSampling(canvas, scene, t); break;
                default: throw new NotSupportedException(scene.Kind.ToString());
            }
        }

        // Every variant translates to the item, so the clip and the circle share one space. The clip is smaller than
        // the circle, so it cuts every item.
        private void DrawGuideClip(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            using var clipRoundRect = new SKRoundRect();
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float s = items[i].Size;
                var clipRect = new SKRect(-s * 0.8f, -s * 0.6f, s * 0.8f, s * 0.6f);
                canvas.Save();
                canvas.Translate(pos.X, pos.Y);
                switch (scene.Variant)
                {
                    case GuideVariant.ClipRect:
                        canvas.ClipRect(clipRect, antialias: _antialias);
                        break;
                    case GuideVariant.ClipRoundRect:
                        clipRoundRect.SetRect(clipRect, s * 0.3f, s * 0.3f);
                        canvas.ClipRoundRect(clipRoundRect, antialias: _antialias);
                        break;
                    case GuideVariant.ClipPath:
                        canvas.ClipPath(_hexagons[i], antialias: _antialias);
                        break;
                }
                _paint.Color = Solid(i);
                canvas.DrawCircle(0f, 0f, s, _paint);
                canvas.Restore();
            }
        }

        // A rounded rectangle with a circle on it, half transparent as a whole.
        private void DrawGuideItemAlpha(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            bool layer = scene.Variant == GuideVariant.SaveLayer;
            byte alpha = layer ? (byte)255 : (byte)128;
            _layerPaint.Color = new SKColor(0, 0, 0, 128);
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float s = items[i].Size;
                var rect = new SKRect(pos.X - s, pos.Y - s * 0.7f, pos.X + s, pos.Y + s * 0.7f);
                if (layer)
                    canvas.SaveLayer(rect, _layerPaint);
                _paint.Color = Solid(i).WithAlpha(alpha);
                canvas.DrawRoundRect(rect, s * 0.3f, s * 0.3f, _paint);
                _paint.Color = Solid(i + 4).WithAlpha(alpha);
                canvas.DrawCircle(pos.X, pos.Y, s * 0.5f, _paint);
                if (layer)
                    canvas.Restore();
            }
        }

        private void DrawGuideDrawOrder(SKCanvas canvas, PerfScene scene, float t)
        {
            int count = scene.Count;
            if (scene.Variant == GuideVariant.Grouped)
            {
                for (int type = 0; type < 4; type++)
                    for (int i = type; i < count; i += 4)
                        DrawOrderItem(canvas, scene, t, i);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    DrawOrderItem(canvas, scene, t, i);
            }
        }

        private void DrawOrderItem(SKCanvas canvas, PerfScene scene, float t, int i)
        {
            var pos = scene.Position(i, t);
            float s = scene.Items[i].Size;
            _paint.Color = Solid(i);
            switch (i % 4)
            {
                case 0:
                    canvas.DrawCircle(pos.X, pos.Y, s, _paint);
                    break;
                case 1:
                    canvas.DrawRect(pos.X - s, pos.Y - s * 0.7f, s * 2f, s * 1.4f, _paint);
                    break;
                case 2:
                {
                    var font = Font(12);
                    canvas.DrawText(_shortLabels[i], pos.X, pos.Y - font.Metrics.Ascent, SKTextAlign.Left, font, _paint);
                    break;
                }
                default:
                    canvas.DrawImage(_gpuImages![0], new SKRect(pos.X - s, pos.Y - s, pos.X + s, pos.Y + s), Linear, _paint);
                    break;
            }
        }

        private void DrawGuidePaintReuse(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                if (scene.Variant == GuideVariant.NewPerDraw)
                {
                    using var paint = new SKPaint { IsAntialias = _antialias, Color = Solid(i) };
                    canvas.DrawCircle(pos.X, pos.Y, items[i].Size, paint);
                }
                else
                {
                    _paint.Color = Solid(i);
                    canvas.DrawCircle(pos.X, pos.Y, items[i].Size, _paint);
                }
            }
        }

        private void DrawGuideFontReuse(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            var cached = Font(PerfScene.SingleFontSize);
            var edging = _antialias ? SKFontEdging.SubpixelAntialias : SKFontEdging.Alias;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                _paint.Color = Solid(i);
                if (scene.Variant == GuideVariant.NewPerDraw)
                {
                    using var font = new SKFont(_typeface, PerfScene.SingleFontSize) { Edging = edging };
                    canvas.DrawText(PerfScene.Label(i), pos.X, pos.Y - font.Metrics.Ascent, SKTextAlign.Left, font, _paint);
                }
                else
                {
                    canvas.DrawText(PerfScene.Label(i), pos.X, pos.Y - cached.Metrics.Ascent, SKTextAlign.Left, cached, _paint);
                }
            }
        }

        private void DrawGuidePathReuse(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            bool rebuild = scene.Variant == GuideVariant.NewPerDraw;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                _paint.Color = Solid(i);
                canvas.Save();
                canvas.Translate(pos.X, pos.Y);
                if (rebuild)
                {
                    using var star = Star(items[i].Size);
                    canvas.DrawPath(star, _paint);
                }
                else
                {
                    canvas.DrawPath(_stars[i], _paint);
                }
                canvas.Restore();
            }
        }

        // Circles, rectangles and rounded rectangles in turn, axis-aligned and with no canvas transform.
        private void DrawAxisAlignedShapes(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float s = items[i].Size;
                _paint.Color = Solid(i);
                switch (i % 3)
                {
                    case 0:
                        canvas.DrawCircle(pos.X, pos.Y, s, _paint);
                        break;
                    case 1:
                        canvas.DrawRect(pos.X - s, pos.Y - s * 0.7f, s * 2f, s * 1.4f, _paint);
                        break;
                    default:
                        float r = PerfScene.CornerRadius(s);
                        canvas.DrawRoundRect(pos.X - s, pos.Y - s * 0.7f, s * 2f, s * 1.4f, r, r, _paint);
                        break;
                }
            }
        }

        // A UI panel with a soft shadow offset down by 4 px: blurred with a mask filter, drawn by a drop-shadow image
        // filter on the panel's own paint, or stretched from a pre-blurred nine-slice image.
        private void DrawGuideShadow(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            for (int i = 0; i < items.Length; i++)
            {
                float s = items[i].Size;
                float w = s * 3f, h = s * 1.6f;
                var pos = scene.Position(i, t);
                var rect = new SKRect(pos.X - w / 2f, pos.Y - h / 2f, pos.X + w / 2f, pos.Y + h / 2f);

                switch (scene.Variant)
                {
                    case GuideVariant.MaskBlur:
                        _paint.MaskFilter = _uiShadowBlur;
                        _paint.Color = GuideShadowColor;
                        canvas.DrawRoundRect(Shifted(rect, 0f, GuideShadowDy), CornerRadius, CornerRadius, _paint);
                        _paint.MaskFilter = null;
                        break;
                    case GuideVariant.NineSlice:
                    {
                        var shadow = Shifted(rect, 0f, GuideShadowDy);
                        shadow.Inflate(_shadowMargin, _shadowMargin);
                        _paint.Color = SKColors.White;
                        canvas.DrawImageLattice(_shadowImage!, _shadowLattice, shadow, SKFilterMode.Linear, _paint);
                        break;
                    }
                    case GuideVariant.DropShadowFilter:
                        _paint.ImageFilter = _dropShadow;
                        break;
                }
                _paint.Color = GuidePanelColor;
                canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, _paint);
                _paint.ImageFilter = null;
            }
        }

        private void DrawGuideStroke(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            _paint.Style = scene.Variant == GuideVariant.Fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
            _paint.StrokeWidth = scene.Variant switch
            {
                GuideVariant.Stroke1 => 1f,
                GuideVariant.Stroke4 => 4f,
                _ => 0f,
            };
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float s = items[i].Size;
                _paint.Color = Solid(i);
                if (i % 2 == 0)
                {
                    canvas.DrawCircle(pos.X, pos.Y, s, _paint);
                }
                else
                {
                    float r = PerfScene.CornerRadius(s);
                    canvas.DrawRoundRect(pos.X - s, pos.Y - s * 0.7f, s * 2f, s * 1.4f, r, r, _paint);
                }
            }
            _paint.Style = SKPaintStyle.Fill;
            _paint.StrokeWidth = 0f;
        }

        private void DrawGuideSampling(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            float half = SamplingImageSize / 2f;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float scale = items[i].Size * 2f / SamplingImageSize;
                _samplingTransforms[i] = SKRotationScaleMatrix.Create(scale, scene.Rotation(i, t), pos.X, pos.Y, half, half);
            }
            var sampling = scene.Variant switch
            {
                GuideVariant.Nearest => new SKSamplingOptions(SKFilterMode.Nearest),
                GuideVariant.LinearMipmaps => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                _ => new SKSamplingOptions(SKFilterMode.Linear),
            };
            canvas.DrawAtlas(_samplingImage!, _samplingSource, _samplingTransforms, sampling, _paint);
        }

        /// <summary>A five-point star centered on the origin, outer radius <paramref name="size"/>.</summary>
        private static SKPath Star(float size) => Polygon(10, size, size * 0.45f);

        /// <summary>A closed polygon around the origin whose corners alternate between the two radii, first corner pointing up.</summary>
        private static SKPath Polygon(int corners, float radius, float alternateRadius)
        {
            var builder = new SKPathBuilder();
            for (int corner = 0; corner < corners; corner++)
            {
                float angle = corner * (MathF.PI * 2f / corners) - MathF.PI / 2f;
                float r = corner % 2 == 0 ? radius : alternateRadius;
                if (corner == 0)
                    builder.MoveTo(MathF.Cos(angle) * r, MathF.Sin(angle) * r);
                else
                    builder.LineTo(MathF.Cos(angle) * r, MathF.Sin(angle) * r);
            }
            builder.Close();
            return builder.Detach();
        }

        // The shadow of a rounded rectangle with CornerRadius corners, blurred once on the CPU. Its center is two
        // pixels, so DrawImageLattice stretches only flat shadow.
        private SKImage BuildShadowImage(GRContext context)
        {
            _shadowMargin = MathF.Ceiling(GuideShadowSigma * 3f);
            int corner = (int)(_shadowMargin + CornerRadius);
            int size = corner * 2 + 2;
            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Transparent);
            using (var paint = new SKPaint { IsAntialias = true, Color = GuideShadowColor, MaskFilter = _uiShadowBlur })
                surface.Canvas.DrawRoundRect(new SKRect(_shadowMargin, _shadowMargin, size - _shadowMargin, size - _shadowMargin), CornerRadius, CornerRadius, paint);
            _shadowLattice = new SKLattice { XDivs = new[] { corner, size - corner }, YDivs = new[] { corner, size - corner } };
            using var raster = surface.Snapshot();
            return raster.ToTextureImage(context) ?? throw new InvalidOperationException("SKImage.ToTextureImage failed.");
        }

        // A detailed 256 px image (a fine checkerboard behind a disc), so minifying it without mipmaps visibly aliases.
        private static SKImage BuildSamplingImage()
        {
            using var surface = SKSurface.Create(new SKImageInfo(SamplingImageSize, SamplingImageSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = true };
            const int cell = 8;
            for (int y = 0; y < SamplingImageSize; y += cell)
                for (int x = 0; x < SamplingImageSize; x += cell)
                {
                    paint.Color = Solid(x / cell + y / cell * 3);
                    canvas.DrawRect(x, y, cell, cell, paint);
                }
            paint.Color = SKColors.White;
            canvas.DrawCircle(SamplingImageSize / 2f, SamplingImageSize / 2f, SamplingImageSize * 0.3f, paint);
            return surface.Snapshot();
        }

        private void DisposeGuide()
        {
            ResetGuideImages();
            _staticShapes?.Dispose();
            foreach (var hexagon in _hexagons)
                hexagon.Dispose();
            foreach (var star in _stars)
                star.Dispose();
            _dropShadow?.Dispose();
            _layerPaint.Dispose();
        }
    }
}
