using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaGameRendering;
using SkiaSharp;

namespace Sample
{
    /// <summary>
    /// Shared game logic for all platform samples. The correct SkiaBackend is
    /// auto-detected at runtime based on which library assembly is referenced.
    /// </summary>
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private SkiaRenderTarget2D _canvas;
        private SKPaint _paint;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = 800;
            _graphics.PreferredBackBufferHeight = 800;

            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _canvas = new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
            _paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            _canvas.Begin();
            _canvas.Canvas.DrawCircle(100, 100, 100, _paint);
            _canvas.End();

            _spriteBatch.Begin(SpriteSortMode.Deferred);
            _spriteBatch.Draw(_canvas, Vector2.Zero, Color.White);
            _spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}
