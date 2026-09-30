using Microsoft.Xna.Framework;
using SkiaGameRendering;
using SkiaSharp;

namespace Performance
{
    /// <summary>
    /// The same sprites drawn through SkiaGameRendering the way the README recommends: one
    /// screen-sized <see cref="SkiaRenderTarget2D"/>, one <see cref="SKCanvas.DrawAtlas(SKImage, SKRect[], SKRotationScaleMatrix[], SKColor[], SKBlendMode, SKSamplingOptions, SKPaint)"/>
    /// per frame on a GPU-resident image, tinted through DrawAtlas's colors, and composited with
    /// <see cref="SkiaRenderTarget2D.End"/>. Everything a game using the library pays per frame is in
    /// the measurement: the context switch, Skia's flush and the composite.
    /// </summary>
    public sealed class SkiaSpriteGame : PerfGame
    {
        private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

        private SkiaRenderTarget2D _target = null!;
        private SKImage _rasterImage = null!;
        private SKImage? _gpuImage;
        private readonly SKPaint _paint = new();

        private SKRect[] _sources = Array.Empty<SKRect>();
        private SKRotationScaleMatrix[] _transforms = Array.Empty<SKRotationScaleMatrix>();
        private SKColor[] _colors = Array.Empty<SKColor>();

        public SkiaSpriteGame(string label, string[] args) : base(label, args) { }

        protected override void LoadRenderer()
        {
            _target = new SkiaRenderTarget2D(GraphicsDevice, Width, Height);
            var info = new SKImageInfo(SpriteTexture.Size, SpriteTexture.Size, SKColorType.Rgba8888, SKAlphaType.Premul);
            _rasterImage = SKImage.FromPixelCopy(info, SpriteTexture.BuildPixels())
                ?? throw new InvalidOperationException("SKImage.FromPixelCopy failed.");
        }

        protected override void DrawScene(SpriteScene scene, float t)
        {
            GraphicsDevice.Clear(Color.Black);

            _target.Begin();
            // Uploaded once: ToTextureImage needs Skia's GPU context current, which it only is
            // between Begin and End.
            _gpuImage ??= _rasterImage.ToTextureImage(SkiaRenderer.CurrentBackend!.GRContext)
                ?? throw new InvalidOperationException("SKImage.ToTextureImage failed.");

            if (scene.Count > 0)
                DrawAtlas(_target.Canvas, scene, t);

            _target.End();
        }

        private void DrawAtlas(SKCanvas canvas, SpriteScene scene, float t)
        {
            if (_sources.Length != scene.Count)
            {
                _sources = new SKRect[scene.Count];
                _transforms = new SKRotationScaleMatrix[scene.Count];
                _colors = new SKColor[scene.Count];
                Array.Fill(_sources, new SKRect(0, 0, SpriteTexture.Size, SpriteTexture.Size));
                for (int i = 0; i < _colors.Length; i++)
                {
                    var c = SpriteScene.TintPalette[i % SpriteScene.TintPalette.Length];
                    _colors[i] = new SKColor(c.R, c.G, c.B, c.A);
                }
            }

            float half = SpriteTexture.Size / 2f;
            var sprites = scene.Sprites;
            for (int i = 0; i < sprites.Length; i++)
            {
                var pos = scene.Position(i, t);
                float scale = sprites[i].Size * 2f / SpriteTexture.Size;
                _transforms[i] = SKRotationScaleMatrix.Create(scale, scene.Rotation(i, t), pos.X, pos.Y, half, half);
            }

            if (scene.Tinted)
                canvas.DrawAtlas(_gpuImage, _sources, _transforms, _colors, SKBlendMode.Modulate, Sampling, _paint);
            else
                canvas.DrawAtlas(_gpuImage, _sources, _transforms, Sampling, _paint);
        }

        protected override void UnloadContent()
        {
            _gpuImage?.Dispose();
            _rasterImage.Dispose();
            _paint.Dispose();
            _target.Dispose();
            SkiaRenderer.Dispose();
        }
    }
}
