using Microsoft.Xna.Framework;
using SkiaSharp;

namespace Test
{
    // Draws itself directly onto whatever shared SkiaRenderTarget2D canvas the caller passes to
    // Draw, at its own screen position - it doesn't own a canvas of its own. Positioning within the
    // canvas is this class's job (the DrawCircle call), the same way a sprite's position is the
    // caller's job when using SpriteBatch, not something the canvas itself tracks.
    internal class SkiaEntity
    {
        private readonly SKPaint _paint;
        private bool _paintNeedsUpdate;
        private SKColor _color = SKColors.Red;

        public Vector3 Position { get; set; }
        public float Radius { get; set; }

        public SKColor Color
        {
            get => _color;
            set { _color = value; _paintNeedsUpdate = true; }
        }

        public SkiaEntity(float radius = 300)
        {
            Radius = radius;
            _paint = new SKPaint { Color = _color, Style = SKPaintStyle.Fill, IsAntialias = true };
        }

        public void Draw(SKCanvas canvas, int centerScreenX, int centerScreenY)
        {
            if (_paintNeedsUpdate)
            {
                _paint.Color = Color;
                _paintNeedsUpdate = false;
            }

            canvas.DrawCircle(centerScreenX + Position.X, centerScreenY + Position.Y, Radius, _paint);
        }
    }
}
