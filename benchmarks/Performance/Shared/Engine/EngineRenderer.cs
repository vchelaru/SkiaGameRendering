using Apos.Shapes;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Performance
{
    /// <summary>
    /// The baseline: the engine's own drawing, with no Skia in the process. <see cref="SpriteBatch"/>
    /// for textures, <c>Apos.Shapes</c> for shapes, gradients, shadows and SVG, a baked
    /// <see cref="SpriteFont"/> and FontStashSharp for text.
    /// </summary>
    public sealed class EngineRenderer : IDisposable
    {
        public const string SpriteBatchLabel = "SpriteBatch";
        public const string InOrderLabel = "SpriteBatch (in order)";
        public const string SortedLabel = "SpriteBatch (sorted by texture)";
        public const string AposLabel = "Apos.Shapes";
        public const string SpriteFontLabel = "SpriteFont";
        public const string FontStashLabel = "FontStashSharp";
        public const string AposSvgLabel = "Apos.Shapes SVG";
        public const string SvgTextureLabel = "SpriteBatch (pre-rasterized SVG)";
        public const string UiLabel = "Apos.Shapes + SpriteBatch";

        private const float ShadowBlur = 12f;
        private const float CornerRadius = 8f;

        private readonly GraphicsDevice _device;
        private readonly SpriteBatch _spriteBatch;
        private readonly ShapeBatch _shapes;
        private readonly Texture2D _sprite;
        private readonly Texture2D[] _textures;
        private readonly Texture2D _pixel;
        private readonly Color[] _tints;
        private readonly Color[] _solid;
        private readonly byte[] _ttf = PerfAssets.FontBytes;
        private readonly Dictionary<int, SpriteFont> _spriteFonts = new();
        private readonly FontSystem _fontSystem = new();
        private readonly ShapeSvg _svg;
        private RenderTarget2D? _svgTexture;
        private const int SvgTextureSize = 128;

        public EngineRenderer(GraphicsDevice device)
        {
            _device = device;
            _spriteBatch = new SpriteBatch(device);
            _shapes = new ShapeBatch(device);

            _sprite = Create(PerfAssets.BuildPixels());
            _textures = new Texture2D[PerfScene.TextureCount];
            for (int i = 0; i < _textures.Length; i++)
                _textures[i] = Create(PerfAssets.BuildTexturePixels(i));
            _pixel = new Texture2D(device, 1, 1, false, SurfaceFormat.Color);
            _pixel.SetData(new[] { Color.White });

            // SpriteBatch expects premultiplied color; the palette is unpremultiplied like SKColor.
            _tints = Array.ConvertAll(PerfScene.TintPalette, c => Color.FromNonPremultiplied(c.R, c.G, c.B, c.A));
            _solid = Array.ConvertAll(PerfScene.SolidPalette, c => new Color(c.R, c.G, c.B, (byte)255));

            _fontSystem.AddFont(_ttf);
            _svg = new ShapeSvg(PerfAssets.SvgBytes);
        }

        private Texture2D Create(byte[] pixels)
        {
            var texture = new Texture2D(_device, PerfAssets.Size, PerfAssets.Size, false, SurfaceFormat.Color);
            texture.SetData(pixels);
            return texture;
        }

        public static IEnumerable<string> Renderers(PerfScene scene)
        {
            switch (scene.Kind)
            {
                case SceneKind.Sprites: return new[] { SpriteBatchLabel };
                case SceneKind.MultiTexture: return new[] { InOrderLabel, SortedLabel };
                case SceneKind.TextSingle:
                case SceneKind.TextMixed: return new[] { SpriteFontLabel, FontStashLabel };
                case SceneKind.Svg: return new[] { AposSvgLabel, SvgTextureLabel };
                case SceneKind.FillOpaque:
                case SceneKind.FillTranslucent: return new[] { SpriteBatchLabel, AposLabel };
                case SceneKind.Ui: return new[] { UiLabel };
                default: return new[] { AposLabel };
            }
        }

        public void Draw(PerfScene scene, string renderer, float t)
        {
            _device.Clear(Color.Black);
            switch (scene.Kind)
            {
                case SceneKind.Sprites: DrawSprites(scene, t); break;
                case SceneKind.MultiTexture: DrawMultiTexture(scene, t, renderer == SortedLabel); break;
                case SceneKind.Circles: DrawCircles(scene, t); break;
                case SceneKind.Shapes: DrawShapes(scene, t); break;
                case SceneKind.Gradients: DrawGradients(scene, t); break;
                case SceneKind.Shadows: DrawShadows(scene, t); break;
                case SceneKind.TextSingle: DrawText(scene, t, renderer, mixed: false); break;
                case SceneKind.TextMixed: DrawText(scene, t, renderer, mixed: true); break;
                case SceneKind.Svg: DrawSvgs(scene, t, renderer == SvgTextureLabel); break;
                case SceneKind.FillOpaque: DrawFill(scene, t, renderer, translucent: false); break;
                case SceneKind.FillTranslucent: DrawFill(scene, t, renderer, translucent: true); break;
                case SceneKind.Ui: DrawUi(scene, t); break;
                default: throw new NotSupportedException(scene.Kind.ToString());
            }
        }

        private void DrawSprites(PerfScene scene, float t)
        {
            var origin = new Vector2(PerfAssets.Size / 2f);
            var items = scene.Items;
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
            for (int i = 0; i < items.Length; i++)
            {
                float scale = items[i].Size * 2f / PerfAssets.Size;
                var tint = scene.Tinted ? _tints[i % _tints.Length] : Color.White;
                _spriteBatch.Draw(_sprite, scene.Position(i, t), null, tint, scene.Rotation(i, t), origin, scale, SpriteEffects.None, 0f);
            }
            _spriteBatch.End();
        }

        private void DrawMultiTexture(PerfScene scene, float t, bool sorted)
        {
            var origin = new Vector2(PerfAssets.Size / 2f);
            var items = scene.Items;
            _spriteBatch.Begin(sorted ? SpriteSortMode.Texture : SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
            for (int i = 0; i < items.Length; i++)
            {
                float scale = items[i].Size * 2f / PerfAssets.Size;
                _spriteBatch.Draw(_textures[i % _textures.Length], scene.Position(i, t), null, Color.White, scene.Rotation(i, t), origin, scale, SpriteEffects.None, 0f);
            }
            _spriteBatch.End();
        }

        private void DrawCircles(PerfScene scene, float t)
        {
            var items = scene.Items;
            _shapes.Begin();
            for (int i = 0; i < items.Length; i++)
                _shapes.FillCircle(scene.Position(i, t), items[i].Size, _solid[i % _solid.Length]);
            _shapes.End();
        }

        private void DrawShapes(PerfScene scene, float t)
        {
            var items = scene.Items;
            _shapes.Begin();
            for (int i = 0; i < items.Length; i++)
            {
                var pos = scene.Position(i, t);
                float s = items[i].Size;
                var fill = _solid[i % _solid.Length];
                var border = _solid[(i + 3) % _solid.Length];
                switch (i % 4)
                {
                    case 0:
                        _shapes.FillCircle(pos, s, fill);
                        break;
                    case 1:
                    {
                        var size = new Vector2(s * 2f, s * 1.4f);
                        _shapes.DrawRectangle(pos - size / 2f, size, fill, border, 2f, s * 0.3f, scene.Rotation(i, t));
                        break;
                    }
                    case 2:
                    {
                        var offset = new Vector2(MathF.Cos(scene.Rotation(i, t)), MathF.Sin(scene.Rotation(i, t))) * s * 1.5f;
                        _shapes.FillLine(pos - offset, pos + offset, 2f, fill);
                        break;
                    }
                    default:
                        _shapes.FillEquilateralTriangle(pos, s, fill, 0f, scene.Rotation(i, t));
                        break;
                }
            }
            _shapes.End();
        }

        private void DrawGradients(PerfScene scene, float t)
        {
            var items = scene.Items;
            _shapes.Begin();
            for (int i = 0; i < items.Length; i++)
            {
                float s = items[i].Size;
                var size = new Vector2(s * 2f, s * 1.4f);
                // Corner to corner in the rectangle's own space, so the gradient turns with it.
                var gradient = new Gradient(
                    -size / 2f, _solid[i % _solid.Length],
                    size / 2f, _solid[(i + 4) % _solid.Length],
                    isLocal: true);
                _shapes.FillRectangle(scene.Position(i, t) - size / 2f, size, gradient, s * 0.3f, scene.Rotation(i, t));
            }
            _shapes.End();
        }

        private void DrawShadows(PerfScene scene, float t)
        {
            var items = scene.Items;
            var shadow = new Color(0, 0, 0, 140);
            _shapes.Begin();
            for (int i = 0; i < items.Length; i++)
            {
                float s = items[i].Size;
                var size = new Vector2(s * 2f, s * 1.4f);
                var xy = scene.Position(i, t) - size / 2f;
                _shapes.FillRectangleBlurred(xy + new Vector2(4f, 6f), size, shadow, ShadowBlur, CornerRadius);
                _shapes.FillRectangle(xy, size, _solid[i % _solid.Length], CornerRadius);
            }
            _shapes.End();
        }

        private SpriteFont GetSpriteFont(int size)
        {
            if (!_spriteFonts.TryGetValue(size, out var font))
                _spriteFonts[size] = font = SpriteFontBaker.Bake(_device, _ttf, size);
            return font;
        }

        /// <summary>Bakes every size the text scenes use, outside the measured window.</summary>
        public void PrepareText(PerfScene scene)
        {
            if (scene.Kind == SceneKind.TextSingle)
            {
                GetSpriteFont(PerfScene.SingleFontSize);
                _fontSystem.GetFont(PerfScene.SingleFontSize);
            }
            else if (scene.Kind == SceneKind.TextMixed)
            {
                foreach (int size in PerfScene.MixedFontSizes)
                {
                    GetSpriteFont(size);
                    _fontSystem.GetFont(size);
                }
            }
            else if (scene.Kind == SceneKind.Ui)
            {
                GetSpriteFont(16);
            }
            else if (scene.Kind == SceneKind.Svg && _svgTexture == null)
            {
                // Rasterized once with Apos.Shapes, so the SVG is not drawn as vectors in the measured frames.
                _svgTexture = new RenderTarget2D(_device, SvgTextureSize, SvgTextureSize, false, SurfaceFormat.Color, DepthFormat.None);
                _device.SetRenderTarget(_svgTexture);
                _device.Clear(Color.Transparent);
                _shapes.Begin();
                _shapes.DrawSvg(_svg, Vector2.Zero, SvgTextureSize);
                _shapes.End();
                _device.SetRenderTarget(null);
            }
        }

        private void DrawText(PerfScene scene, float t, string renderer, bool mixed)
        {
            var items = scene.Items;
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
            for (int i = 0; i < items.Length; i++)
            {
                int size = mixed ? PerfScene.MixedFontSize(i) : PerfScene.SingleFontSize;
                var color = _solid[i % _solid.Length];
                var pos = scene.Position(i, t);
                string text = PerfScene.Label(i);
                if (renderer == FontStashLabel)
                    _fontSystem.GetFont(size).DrawText(_spriteBatch, text, pos, color);
                else
                    _spriteBatch.DrawString(GetSpriteFont(size), text, pos, color);
            }
            _spriteBatch.End();
        }

        private void DrawSvgs(PerfScene scene, float t, bool texture)
        {
            var items = scene.Items;
            if (texture)
            {
                var origin = new Vector2(SvgTextureSize / 2f);
                _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
                for (int i = 0; i < items.Length; i++)
                    _spriteBatch.Draw(_svgTexture, scene.Position(i, t), null, Color.White, 0f, origin, items[i].Size * 2f / SvgTextureSize, SpriteEffects.None, 0f);
                _spriteBatch.End();
                return;
            }

            _shapes.Begin();
            for (int i = 0; i < items.Length; i++)
            {
                // The size is the viewBox height in pixels; the position is the top left.
                float side = items[i].Size * 2f;
                _shapes.DrawSvg(_svg, scene.Position(i, t) - new Vector2(side / 2f), side);
            }
            _shapes.End();
        }

        private void DrawFill(PerfScene scene, float t, string renderer, bool translucent)
        {
            var items = scene.Items;
            float w = scene.Width, h = scene.Height;
            var center = new Vector2(w / 2f, h / 2f);
            byte alpha = translucent ? (byte)64 : (byte)255;
            if (renderer == AposLabel)
            {
                _shapes.Begin(blendState: translucent ? BlendState.AlphaBlend : BlendState.Opaque);
                for (int i = 0; i < items.Length; i++)
                {
                    var size = new Vector2(w, h) * items[i].Size;
                    var c = _solid[i % _solid.Length];
                    var color = translucent ? Color.FromNonPremultiplied(c.R, c.G, c.B, alpha) : c;
                    var xy = center + (scene.Position(i, t) - items[i].BasePosition) - size / 2f;
                    _shapes.FillRectangle(xy, size, color);
                }
                _shapes.End();
            }
            else
            {
                _spriteBatch.Begin(SpriteSortMode.Deferred, translucent ? BlendState.AlphaBlend : BlendState.Opaque, SamplerState.LinearClamp);
                for (int i = 0; i < items.Length; i++)
                {
                    var size = new Vector2(w, h) * items[i].Size;
                    var c = _solid[i % _solid.Length];
                    var color = translucent ? Color.FromNonPremultiplied(c.R, c.G, c.B, alpha) : c;
                    var xy = center + (scene.Position(i, t) - items[i].BasePosition) - size / 2f;
                    _spriteBatch.Draw(_pixel, new Rectangle((int)xy.X, (int)xy.Y, (int)size.X, (int)size.Y), color);
                }
                _spriteBatch.End();
            }
        }

        private void DrawUi(PerfScene scene, float t)
        {
            var items = scene.Items;
            var shadow = new Color(0, 0, 0, 120);
            var panel = new Color(38, 42, 54);
            var border = new Color(90, 98, 120);

            _shapes.Begin();
            for (int i = 0; i < items.Length; i++)
            {
                float s = items[i].Size;
                var size = new Vector2(s * 3f, s * 1.6f);
                var xy = scene.Position(i, t) - size / 2f;
                _shapes.FillRectangleBlurred(xy + new Vector2(0f, 4f), size, shadow, 10f, CornerRadius);
                _shapes.DrawRectangle(xy, size, panel, border, 1.5f, CornerRadius);
            }
            _shapes.End();

            var font = GetSpriteFont(16);
            var origin = new Vector2(PerfAssets.Size / 2f);
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
            for (int i = 0; i < items.Length; i++)
            {
                float s = items[i].Size;
                var size = new Vector2(s * 3f, s * 1.6f);
                var xy = scene.Position(i, t) - size / 2f;
                var iconCenter = xy + new Vector2(s * 0.8f, size.Y / 2f);
                _spriteBatch.Draw(_textures[i % _textures.Length], iconCenter, null, Color.White, 0f, origin, s * 0.9f / PerfAssets.Size, SpriteEffects.None, 0f);
                _spriteBatch.DrawString(font, $"Item {i}", xy + new Vector2(s * 1.5f, size.Y / 2f - font.LineSpacing / 2f), Color.White);
            }
            _spriteBatch.End();
        }

        public void Dispose()
        {
            _sprite.Dispose();
            foreach (var texture in _textures)
                texture.Dispose();
            _pixel.Dispose();
            _svgTexture?.Dispose();
            _spriteBatch.Dispose();
            _shapes.Dispose();
            _fontSystem.Dispose();
            foreach (var font in _spriteFonts.Values)
                font.Texture.Dispose();
        }
    }
}
