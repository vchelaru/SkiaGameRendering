using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Performance
{
    /// <summary>The baseline: plain MonoGame <see cref="SpriteBatch"/>, no Skia anywhere in the process.</summary>
    public sealed class RawSpriteGame : PerfGame
    {
        private SpriteBatch _spriteBatch = null!;
        private Texture2D _texture = null!;
        private Color[] _tints = null!;

        public RawSpriteGame(string label, string[] args) : base(label, args) { }

        protected override void LoadRenderer()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _texture = new Texture2D(GraphicsDevice, SpriteTexture.Size, SpriteTexture.Size, false, SurfaceFormat.Color);
            _texture.SetData(SpriteTexture.BuildPixels());

            // SpriteBatch expects premultiplied color; the palette is unpremultiplied like SKColor.
            _tints = Array.ConvertAll(SpriteScene.TintPalette, c => Color.FromNonPremultiplied(c.R, c.G, c.B, c.A));
        }

        protected override void DrawScene(SpriteScene scene, float t)
        {
            GraphicsDevice.Clear(Color.Black);

            var origin = new Vector2(SpriteTexture.Size / 2f);
            var sprites = scene.Sprites;
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
            for (int i = 0; i < sprites.Length; i++)
            {
                float scale = sprites[i].Size * 2f / SpriteTexture.Size;
                var tint = scene.Tinted ? _tints[i % _tints.Length] : Color.White;
                _spriteBatch.Draw(_texture, scene.Position(i, t), null, tint, scene.Rotation(i, t), origin, scale, SpriteEffects.None, 0f);
            }
            _spriteBatch.End();
        }

        protected override void UnloadContent()
        {
            _texture.Dispose();
            _spriteBatch.Dispose();
        }
    }
}
