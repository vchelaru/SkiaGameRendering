using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaGameRendering;
using SkiaSharp;

namespace Sample
{
    /// <summary>
    /// Shared game logic for all platform samples. The backend is constructed by each platform's
    /// own Program.cs and passed in - same shape as the WebGL sample's Game1, whose backend must be
    /// constructed and awaited (via <see cref="SkiaBackend.Ready"/>) before Run().
    /// </summary>
    public class Game1 : Game
    {
        private readonly SkiaBackend _backend;
        private GraphicsDeviceManager _graphics;
        private SkiaRenderTarget2D _canvas;
        private SKPaint _paint;

        public Game1(SkiaBackend backend)
        {
            _backend = backend;
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = 800;
            _graphics.PreferredBackBufferHeight = 800;

            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            SkiaRenderer.Initialize(_backend, GraphicsDevice);
            base.Initialize();
        }

        protected override void LoadContent()
        {
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

            base.Draw(gameTime);
        }
    }
}
