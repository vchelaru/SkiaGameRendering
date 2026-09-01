using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaGameRendering;
using SkiaSharp;

namespace Sample.Kni.DesktopGL
{
    /// <summary>
    /// Shared game logic for all platform samples. Never references a specific SkiaBackend type or
    /// takes one through its constructor - SkiaRenderer.IsReady/Initialize(GraphicsDevice) are
    /// declared on SkiaRenderer's shared, platform-agnostic part, so this exact code also compiles
    /// and behaves correctly on KNI WebGL (see samples/Sample.Kni.WebGL/Game1.cs), where IsReady
    /// reflects a real async host readiness check instead of always being true.
    /// </summary>
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
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
            if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
                SkiaRenderer.Initialize(GraphicsDevice);

            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            if (SkiaRenderer.IsInitialized)
            {
                _canvas ??= new SkiaRenderTarget2D(GraphicsDevice, 200, 200);
                _canvas.Begin();
                _canvas.Canvas.DrawCircle(100, 100, 100, _paint);
                _canvas.End();
            }

            base.Draw(gameTime);
        }
    }
}
