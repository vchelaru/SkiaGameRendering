using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace Benchmarks.ShapeRendering
{
    /// <summary>
    /// Draws a sprite <see cref="Scene"/> three ways from the same pixels: Skia one
    /// <see cref="SKCanvas.DrawImage(SKImage, SKRect, SKSamplingOptions, SKPaint)"/> per sprite, Skia
    /// one <see cref="SKCanvas.DrawAtlas(SKImage, SKRect[], SKRotationScaleMatrix[], SKSamplingOptions, SKPaint)"/>
    /// for the whole scene, and <see cref="SpriteBatch"/>. All three use one texture, linear
    /// filtering, no edge antialiasing and the same position/rotation/size per sprite, which is
    /// SpriteBatch's best case and Skia's best case alike.
    /// </summary>
    internal sealed class SpriteRenderers : IDisposable
    {
        private const int TextureSize = 64;
        private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

        private readonly Texture2D _texture;
        private readonly SKImage _rasterImage;
        private SKImage? _gpuImage;
        private readonly SKPaint _paint = new();

        private SKRect[] _atlasSources = Array.Empty<SKRect>();
        private SKRotationScaleMatrix[] _atlasTransforms = Array.Empty<SKRotationScaleMatrix>();

        public SpriteRenderers(GraphicsDevice graphicsDevice)
        {
            var pixels = BuildPixels();

            _texture = new Texture2D(graphicsDevice, TextureSize, TextureSize, false, SurfaceFormat.Color);
            _texture.SetData(pixels);

            var info = new SKImageInfo(TextureSize, TextureSize, SKColorType.Rgba8888, SKAlphaType.Premul);
            _rasterImage = SKImage.FromPixelCopy(info, pixels)
                ?? throw new InvalidOperationException("SKImage.FromPixelCopy failed.");
        }

        public void DrawSkia(SKCanvas canvas, GRContext grContext, Scene scene, float t)
        {
            var image = GetGpuImage(grContext);
            var shapes = scene.Shapes;
            for (int i = 0; i < shapes.Length; i++)
            {
                var pos = scene.AnimatedPosition(i, t);
                float size = shapes[i].Size;

                // Save/transform/Restore per sprite is how Skia draws a rotated image without DrawAtlas.
                canvas.Save();
                canvas.Translate(pos.X, pos.Y);
                canvas.RotateRadians(scene.AnimatedRotation(i, t));
                canvas.DrawImage(image, new SKRect(-size, -size, size, size), Sampling, _paint);
                canvas.Restore();
            }
        }

        public void DrawSkiaAtlas(SKCanvas canvas, GRContext grContext, Scene scene, float t)
        {
            if (scene.Count == 0)
                return;

            var image = GetGpuImage(grContext);
            if (_atlasSources.Length != scene.Count)
            {
                _atlasSources = new SKRect[scene.Count];
                _atlasTransforms = new SKRotationScaleMatrix[scene.Count];
                Array.Fill(_atlasSources, new SKRect(0, 0, TextureSize, TextureSize));
            }

            float half = TextureSize / 2f;
            var shapes = scene.Shapes;
            for (int i = 0; i < shapes.Length; i++)
            {
                var pos = scene.AnimatedPosition(i, t);
                float scale = shapes[i].Size * 2f / TextureSize;
                _atlasTransforms[i] = SKRotationScaleMatrix.Create(
                    scale, scene.AnimatedRotation(i, t), pos.X, pos.Y, half, half);
            }

            canvas.DrawAtlas(image, _atlasSources, _atlasTransforms, Sampling, _paint);
        }

        public void DrawSpriteBatch(SpriteBatch spriteBatch, Scene scene, float t)
        {
            var origin = new Vector2(TextureSize / 2f);
            var shapes = scene.Shapes;

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
            for (int i = 0; i < shapes.Length; i++)
            {
                float scale = shapes[i].Size * 2f / TextureSize;
                spriteBatch.Draw(_texture, scene.AnimatedPosition(i, t), null, Color.White,
                    scene.AnimatedRotation(i, t), origin, scale, SpriteEffects.None, 0f);
            }
            spriteBatch.End();
        }

        // Uploaded once, on first use: ToTextureImage needs Skia's GPU context current, which it only
        // is between SkiaRenderTarget2D.Begin and End. Drawing the raster image directly would leave
        // the upload and caching to Skia instead of measuring the recommended GPU-resident path.
        private SKImage GetGpuImage(GRContext grContext) =>
            _gpuImage ??= _rasterImage.ToTextureImage(grContext)
                ?? throw new InvalidOperationException("SKImage.ToTextureImage failed.");

        // A soft-edged disc with a lighter ring, premultiplied so SpriteBatch's AlphaBlend and Skia's
        // Premul image blend the same pixels identically.
        private static byte[] BuildPixels()
        {
            var pixels = new byte[TextureSize * TextureSize * 4];
            float center = (TextureSize - 1) / 2f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float d = MathF.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / center;
                    float alpha = Math.Clamp((1f - d) * 8f, 0f, 1f);
                    float shade = d > 0.7f ? 1f : 0.55f;
                    int o = (y * TextureSize + x) * 4;
                    pixels[o + 0] = (byte)(90 * shade * alpha);
                    pixels[o + 1] = (byte)(170 * shade * alpha);
                    pixels[o + 2] = (byte)(255 * shade * alpha);
                    pixels[o + 3] = (byte)(255 * alpha);
                }
            }
            return pixels;
        }

        public void Dispose()
        {
            _gpuImage?.Dispose();
            _rasterImage.Dispose();
            _paint.Dispose();
            _texture.Dispose();
        }
    }
}
