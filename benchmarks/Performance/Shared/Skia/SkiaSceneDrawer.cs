using Microsoft.Xna.Framework;
using SkiaSharp;
using Svg.Skia;

namespace Performance
{
    /// <summary>
    /// Draws every scene with plain <see cref="SKCanvas"/> calls: nothing from the engine and no
    /// Apos.Shapes. Sprites go through <c>DrawAtlas</c> on GPU-resident images, as the README recommends.
    /// It draws onto whatever canvas it is handed, so the same code runs under a
    /// <c>SkiaRenderTarget2D</c> and under a <c>SkiaDirectCanvas</c>.
    /// </summary>
    public sealed class SkiaSceneDrawer : IDisposable
    {
        private const float ShadowBlur = 12f;
        private const float CornerRadius = 8f;
        private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear);

        private readonly SKPaint _paint = new() { IsAntialias = true };
        private readonly SKImage[] _rasterImages;
        private SKImage[]? _gpuImages;
        private readonly SKTypeface _typeface;
        private readonly Dictionary<int, SKFont> _fonts = new();
        private readonly SKSvg _svg = new();
        private readonly SKPicture _svgPicture;
        private readonly SKMaskFilter _shadowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, ShadowBlur / 2f);
        private readonly SKMaskFilter _uiShadowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f);
        private readonly SKPath _triangle;
        private const int SvgImageSize = 128;
        private SKImage? _svgImage;
        private SKRect[] _svgSource = Array.Empty<SKRect>();
        private SKRotationScaleMatrix[] _svgTransforms = Array.Empty<SKRotationScaleMatrix>();

        // DrawAtlas inputs, one set per texture so each texture is a single call.
        private SKRect[][] _sources = Array.Empty<SKRect[]>();
        private SKRotationScaleMatrix[][] _transforms = Array.Empty<SKRotationScaleMatrix[]>();
        private SKColor[] _tintColors = Array.Empty<SKColor>();

        private SKShader[]? _gradients;
        private PerfScene? _preparedScene;

        public SkiaSceneDrawer()
        {
            var info = new SKImageInfo(PerfAssets.Size, PerfAssets.Size, SKColorType.Rgba8888, SKAlphaType.Premul);
            _rasterImages = new SKImage[1 + PerfScene.TextureCount];
            _rasterImages[0] = SKImage.FromPixelCopy(info, PerfAssets.BuildPixels())
                ?? throw new InvalidOperationException("SKImage.FromPixelCopy failed.");
            for (int i = 0; i < PerfScene.TextureCount; i++)
            {
                _rasterImages[1 + i] = SKImage.FromPixelCopy(info, PerfAssets.BuildTexturePixels(i))
                    ?? throw new InvalidOperationException("SKImage.FromPixelCopy failed.");
            }

            _typeface = SKTypeface.FromData(SKData.CreateCopy(PerfAssets.FontBytes))
                ?? throw new InvalidOperationException("The font could not be read.");

            using (var stream = new MemoryStream(PerfAssets.SvgBytes))
                _svg.Load(stream);
            _svgPicture = _svg.Picture ?? throw new InvalidOperationException("The SVG could not be read.");

            // An equilateral triangle with a circumradius of 1, pointing up, scaled and rotated through the canvas.
            var builder = new SKPathBuilder();
            for (int corner = 0; corner < 3; corner++)
            {
                float angle = corner * (MathF.PI * 2f / 3f) - MathF.PI / 2f;
                if (corner == 0)
                    builder.MoveTo(MathF.Cos(angle), MathF.Sin(angle));
                else
                    builder.LineTo(MathF.Cos(angle), MathF.Sin(angle));
            }
            builder.Close();
            _triangle = builder.Detach();
        }

        /// <summary>
        /// Uploads the images and builds the per-scene caches. Needs Skia's GPU context current, so call it
        /// between Begin and End, before the measured frames.
        /// </summary>
        public void Prepare(GRContext context, PerfScene scene)
        {
            if (scene.Kind == SceneKind.Svg && _svgImage == null)
            {
                // Rasterized once on the CPU, so the SVG is not replayed as vectors in the measured frames.
                using var surface = SKSurface.Create(new SKImageInfo(SvgImageSize, SvgImageSize, SKColorType.Rgba8888, SKAlphaType.Premul));
                float scale = SvgImageSize / _svgPicture.CullRect.Height;
                surface.Canvas.Scale(scale, scale);
                surface.Canvas.DrawPicture(_svgPicture);
                using var raster = surface.Snapshot();
                _svgImage = raster.ToTextureImage(context) ?? throw new InvalidOperationException("SKImage.ToTextureImage failed.");
            }
            if (scene.Kind == SceneKind.Svg && _svgTransforms.Length != scene.Count)
            {
                _svgTransforms = new SKRotationScaleMatrix[scene.Count];
                _svgSource = new SKRect[scene.Count];
                Array.Fill(_svgSource, new SKRect(0, 0, SvgImageSize, SvgImageSize));
            }

            _gpuImages ??= Array.ConvertAll(_rasterImages, image =>
                image.ToTextureImage(context) ?? throw new InvalidOperationException("SKImage.ToTextureImage failed."));

            if (ReferenceEquals(_preparedScene, scene))
                return;
            _preparedScene = scene;

            if (_gradients != null)
                foreach (var shader in _gradients)
                    shader.Dispose();
            _gradients = null;

            switch (scene.Kind)
            {
                case SceneKind.Gradients:
                    _gradients = new SKShader[scene.Count];
                    for (int i = 0; i < scene.Count; i++)
                    {
                        float s = scene.Items[i].Size;
                        var half = new SKPoint(s, s * 0.7f);
                        _gradients[i] = SKShader.CreateLinearGradient(
                            new SKPoint(-half.X, -half.Y), new SKPoint(half.X, half.Y),
                            new[] { Solid(i), Solid(i + 4) }, SKShaderTileMode.Clamp);
                    }
                    break;

                case SceneKind.Sprites:
                    PrepareAtlas(scene.Count, 1);
                    break;
                case SceneKind.MultiTexture:
                case SceneKind.Ui:
                    PrepareAtlas(scene.Count, PerfScene.TextureCount);
                    break;

                case SceneKind.TextSingle:
                    Font(PerfScene.SingleFontSize);
                    break;
                case SceneKind.TextMixed:
                    foreach (int size in PerfScene.MixedFontSizes)
                        Font(size);
                    break;
            }
            if (scene.Kind == SceneKind.Ui)
                Font(16);
        }

        private void PrepareAtlas(int count, int groups)
        {
            _sources = new SKRect[groups][];
            _transforms = new SKRotationScaleMatrix[groups][];
            for (int k = 0; k < groups; k++)
            {
                int perGroup = (count + groups - 1 - k) / groups;
                _sources[k] = new SKRect[perGroup];
                _transforms[k] = new SKRotationScaleMatrix[perGroup];
                Array.Fill(_sources[k], new SKRect(0, 0, PerfAssets.Size, PerfAssets.Size));
            }
            _tintColors = new SKColor[count];
            for (int i = 0; i < count; i++)
            {
                var c = PerfScene.TintPalette[i % PerfScene.TintPalette.Length];
                _tintColors[i] = new SKColor(c.R, c.G, c.B, c.A);
            }
        }

        private SKFont Font(int size)
        {
            if (!_fonts.TryGetValue(size, out var font))
                _fonts[size] = font = new SKFont(_typeface, size) { Edging = SKFontEdging.SubpixelAntialias };
            return font;
        }

        private static SKRect Shifted(SKRect rect, float dx, float dy) => new(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);

        private static SKColor Solid(int index)
        {
            var c = PerfScene.SolidPalette[index % PerfScene.SolidPalette.Length];
            return new SKColor(c.R, c.G, c.B);
        }

        public void Draw(SKCanvas canvas, PerfScene scene, float t, bool preRasterized = false)
        {
            switch (scene.Kind)
            {
                case SceneKind.Sprites: DrawSprites(canvas, scene, t); break;
                case SceneKind.MultiTexture: DrawMultiTexture(canvas, scene, t); break;
                case SceneKind.Circles: DrawCircles(canvas, scene, t); break;
                case SceneKind.Shapes: DrawShapes(canvas, scene, t); break;
                case SceneKind.Gradients: DrawGradients(canvas, scene, t); break;
                case SceneKind.Shadows: DrawShadows(canvas, scene, t); break;
                case SceneKind.TextSingle: DrawText(canvas, scene, t, mixed: false); break;
                case SceneKind.TextMixed: DrawText(canvas, scene, t, mixed: true); break;
                case SceneKind.Svg: DrawSvgs(canvas, scene, t, preRasterized); break;
                case SceneKind.FillOpaque: DrawFill(canvas, scene, t, translucent: false); break;
                case SceneKind.FillTranslucent: DrawFill(canvas, scene, t, translucent: true); break;
                case SceneKind.Ui: DrawUi(canvas, scene, t); break;
                default: throw new NotSupportedException(scene.Kind.ToString());
            }
        }

        private void DrawSprites(SKCanvas canvas, PerfScene scene, float t)
        {
            if (scene.Count == 0)
                return;

            float half = PerfAssets.Size / 2f;
            var items = scene.Items;
            var transforms = _transforms[0];
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float scale = items[i].Size * 2f / PerfAssets.Size;
                transforms[i] = SKRotationScaleMatrix.Create(scale, scene.Rotation(i, t), pos.X, pos.Y, half, half);
            }

            if (scene.Tinted)
                canvas.DrawAtlas(_gpuImages![0], _sources[0], transforms, _tintColors, SKBlendMode.Modulate, Linear, _paint);
            else
                canvas.DrawAtlas(_gpuImages![0], _sources[0], transforms, Linear, _paint);
        }

        // One DrawAtlas per texture: item i goes in group i % 10, so each call draws every tenth item.
        private void DrawMultiTexture(SKCanvas canvas, PerfScene scene, float t)
        {
            float half = PerfAssets.Size / 2f;
            var items = scene.Items;
            int groups = PerfScene.TextureCount;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float scale = items[i].Size * 2f / PerfAssets.Size;
                _transforms[i % groups][i / groups] = SKRotationScaleMatrix.Create(scale, scene.Rotation(i, t), pos.X, pos.Y, half, half);
            }
            for (int k = 0; k < groups; k++)
            {
                if (_transforms[k].Length > 0)
                    canvas.DrawAtlas(_gpuImages![1 + k], _sources[k], _transforms[k], Linear, _paint);
            }
        }

        private void DrawCircles(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                _paint.Color = Solid(i);
                canvas.DrawCircle(pos.X, pos.Y, items[i].Size, _paint);
            }
        }

        private void DrawShapes(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float s = items[i].Size;
                float rotation = scene.Rotation(i, t);
                _paint.Style = SKPaintStyle.Fill;
                _paint.Color = Solid(i);
                switch (i % 4)
                {
                    case 0:
                        canvas.DrawCircle(pos.X, pos.Y, s, _paint);
                        break;
                    case 1:
                    {
                        canvas.Save();
                        canvas.Translate(pos.X, pos.Y);
                        canvas.RotateRadians(rotation);
                        var rect = new SKRect(-s, -s * 0.7f, s, s * 0.7f);
                        canvas.DrawRoundRect(rect, s * 0.3f, s * 0.3f, _paint);
                        _paint.Style = SKPaintStyle.Stroke;
                        _paint.StrokeWidth = 2f;
                        _paint.Color = Solid(i + 3);
                        canvas.DrawRoundRect(rect, s * 0.3f, s * 0.3f, _paint);
                        canvas.Restore();
                        break;
                    }
                    case 2:
                    {
                        float dx = MathF.Cos(rotation) * s * 1.5f, dy = MathF.Sin(rotation) * s * 1.5f;
                        _paint.Style = SKPaintStyle.Stroke;
                        _paint.StrokeWidth = 4f;
                        _paint.StrokeCap = SKStrokeCap.Round;
                        canvas.DrawLine(pos.X - dx, pos.Y - dy, pos.X + dx, pos.Y + dy, _paint);
                        _paint.StrokeCap = SKStrokeCap.Butt;
                        break;
                    }
                    default:
                        canvas.Save();
                        canvas.Translate(pos.X, pos.Y);
                        canvas.RotateRadians(rotation);
                        canvas.Scale(s, s);
                        canvas.DrawPath(_triangle, _paint);
                        canvas.Restore();
                        break;
                }
            }
            _paint.Style = SKPaintStyle.Fill;
        }

        private void DrawGradients(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float s = items[i].Size;
                canvas.Save();
                canvas.Translate(pos.X, pos.Y);
                canvas.RotateRadians(scene.Rotation(i, t));
                _paint.Shader = _gradients![i];
                canvas.DrawRoundRect(new SKRect(-s, -s * 0.7f, s, s * 0.7f), s * 0.3f, s * 0.3f, _paint);
                canvas.Restore();
            }
            _paint.Shader = null;
        }

        private void DrawShadows(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float s = items[i].Size;
                var rect = new SKRect(pos.X - s, pos.Y - s * 0.7f, pos.X + s, pos.Y + s * 0.7f);

                _paint.MaskFilter = _shadowBlur;
                _paint.Color = new SKColor(0, 0, 0, 140);
                canvas.DrawRoundRect(Shifted(rect, 4f, 6f), CornerRadius, CornerRadius, _paint);
                _paint.MaskFilter = null;
                _paint.Color = Solid(i);
                canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, _paint);
            }
        }

        private void DrawText(SKCanvas canvas, PerfScene scene, float t, bool mixed)
        {
            var items = scene.Items;
            for (int i = 0; i < items.Length; i++)
            {
                var font = Font(mixed ? PerfScene.MixedFontSize(i) : PerfScene.SingleFontSize);
                var pos = scene.Position(i, t);
                _paint.Color = Solid(i);
                // The engine draws text from the top-left; Skia from the baseline.
                canvas.DrawText(PerfScene.Label(i), pos.X, pos.Y - font.Metrics.Ascent, SKTextAlign.Left, font, _paint);
            }
        }

        private void DrawSvgs(SKCanvas canvas, PerfScene scene, float t, bool preRasterized)
        {
            var items = scene.Items;
            if (preRasterized)
            {
                float half = SvgImageSize / 2f;
                for (int i = 0; i < items.Length; i++)
                {
                    var pos = scene.Position(i, t);
                    _svgTransforms[i] = SKRotationScaleMatrix.Create(items[i].Size * 2f / SvgImageSize, 0f, pos.X, pos.Y, half, half);
                }
                if (items.Length > 0)
                    canvas.DrawAtlas(_svgImage!, _svgSource, _svgTransforms, Linear, _paint);
                return;
            }

            var bounds = _svgPicture.CullRect;
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float side = items[i].Size * 2f;
                canvas.Save();
                canvas.Translate(pos.X - side / 2f, pos.Y - side / 2f);
                canvas.Scale(side / bounds.Height, side / bounds.Height);
                canvas.DrawPicture(_svgPicture);
                canvas.Restore();
            }
        }

        private void DrawFill(SKCanvas canvas, PerfScene scene, float t, bool translucent)
        {
            var items = scene.Items;
            float w = scene.Width, h = scene.Height;
            for (int i = 0; i < items.Length; i++)
            {
                float rw = w * items[i].Size, rh = h * items[i].Size;
                float x = w / 2f + (scene.Position(i, t).X - items[i].BasePosition.X) - rw / 2f;
                float y = h / 2f + (scene.Position(i, t).Y - items[i].BasePosition.Y) - rh / 2f;
                var c = Solid(i);
                _paint.Color = translucent ? c.WithAlpha(64) : c;
                canvas.DrawRect(x, y, rw, rh, _paint);
            }
        }

        private void DrawUi(SKCanvas canvas, PerfScene scene, float t)
        {
            var items = scene.Items;
            var panel = new SKColor(38, 42, 54);
            var border = new SKColor(90, 98, 120);
            var shadow = new SKColor(0, 0, 0, 120);

            for (int i = 0; i < items.Length; i++)
            {
                float s = items[i].Size;
                float w = s * 3f, h = s * 1.6f;
                var pos = scene.Position(i, t);
                var rect = new SKRect(pos.X - w / 2f, pos.Y - h / 2f, pos.X + w / 2f, pos.Y + h / 2f);

                _paint.MaskFilter = _uiShadowBlur;
                _paint.Color = shadow;
                canvas.DrawRoundRect(Shifted(rect, 0f, 4f), CornerRadius, CornerRadius, _paint);
                _paint.MaskFilter = null;
                _paint.Color = panel;
                canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, _paint);
                _paint.Style = SKPaintStyle.Stroke;
                _paint.StrokeWidth = 1.5f;
                _paint.Color = border;
                canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, _paint);
                _paint.Style = SKPaintStyle.Fill;
            }

            float half = PerfAssets.Size / 2f;
            int groups = PerfScene.TextureCount;
            for (int i = 0; i < items.Length; i++)
            {
                float s = items[i].Size;
                float h = s * 1.6f;
                var pos = scene.Position(i, t);
                float iconX = pos.X - s * 1.5f + s * 0.8f;
                _transforms[i % groups][i / groups] = SKRotationScaleMatrix.Create(s * 0.9f / PerfAssets.Size, 0f, iconX, pos.Y, half, half);
            }
            for (int k = 0; k < groups; k++)
            {
                if (_transforms[k].Length > 0)
                    canvas.DrawAtlas(_gpuImages![1 + k], _sources[k], _transforms[k], Linear, _paint);
            }

            var font = Font(16);
            _paint.Color = SKColors.White;
            for (int i = 0; i < items.Length; i++)
            {
                float s = items[i].Size;
                float h = s * 1.6f;
                var pos = scene.Position(i, t);
                float x = pos.X - s * 1.5f + s * 1.5f;
                float top = pos.Y - h / 2f + h / 2f - font.Spacing / 2f;
                canvas.DrawText($"Item {i}", x, top - font.Metrics.Ascent, SKTextAlign.Left, font, _paint);
            }
        }

        public void Dispose()
        {
            if (_gradients != null)
                foreach (var shader in _gradients)
                    shader.Dispose();
            _svgImage?.Dispose();
            if (_gpuImages != null)
                foreach (var image in _gpuImages)
                    image.Dispose();
            foreach (var image in _rasterImages)
                image.Dispose();
            foreach (var font in _fonts.Values)
                font.Dispose();
            _svgPicture.Dispose();
            _svg.Dispose();
            _typeface.Dispose();
            _shadowBlur.Dispose();
            _uiShadowBlur.Dispose();
            _triangle.Dispose();
            _paint.Dispose();
        }
    }
}
