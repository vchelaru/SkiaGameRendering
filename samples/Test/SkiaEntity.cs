using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;

namespace Test
{
    internal class SkiaEntity : Entity
    {
        private readonly GraphicsDevice _graphicsDevice;
        private SkiaRenderTarget2D _canvas;
        private readonly SKPaint _paint;
        private bool _paintNeedsUpdate;
        private SKColor _color = SKColors.Red;
        private float _radius;

        public float Radius
        {
            get => _radius;
            set
            {
                if (_radius == value)
                    return;
                _radius = value;
                RecreateCanvas();
            }
        }

        public SKColor Color
        {
            get => _color;
            set { _color = value; _paintNeedsUpdate = true; }
        }

        public SkiaEntity(GraphicsDevice graphicsDevice, float radius = 300)
        {
            _graphicsDevice = graphicsDevice;
            _radius = radius;
            _paint = new SKPaint { Color = _color, Style = SKPaintStyle.Fill, IsAntialias = true };
            RecreateCanvas();
        }

        // SkiaRenderTarget2D is fixed-size for its lifetime (like RenderTarget2D) - a radius change
        // means a new render target, not a resize. Explicit here since the library won't do it
        // silently anymore.
        private void RecreateCanvas()
        {
            _canvas?.Dispose();
            _canvas = new SkiaRenderTarget2D(_graphicsDevice, (int)(_radius * 2), (int)(_radius * 2));
            Texture = _canvas.Texture;
        }

        public void Draw()
        {
            if (_paintNeedsUpdate)
            {
                _paint.Color = Color;
                _paintNeedsUpdate = false;
            }

            _canvas.Begin();
            _canvas.Canvas.DrawCircle(Radius, Radius, Radius, _paint);
            _canvas.End();
        }

        public void Destroy() => _canvas.Dispose();
    }
}
