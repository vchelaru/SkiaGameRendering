using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Sample.Shared;
using SkiaGameRendering;

namespace Sample
{
    /// <summary>
    /// Shared game logic for all platform samples. Never references a specific SkiaBackend type or
    /// takes one through its constructor - SkiaRenderer.IsReady/Initialize(GraphicsDevice) are
    /// declared on SkiaRenderer's shared, platform-agnostic part, so this exact code also compiles
    /// and behaves correctly on KNI WebGL (see samples/Sample.Kni.WebGL/Game1.cs), where IsReady
    /// reflects a real async host readiness check instead of always being true. The actual Skia
    /// drawing lives in <see cref="Scene"/>, shared with every other sample project.
    /// </summary>
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SkiaRenderTarget2D _canvas;
        private readonly bool _smokeTest;
        private int _frameCount;

        public int ExitCode { get; private set; }

        public Game1(bool smokeTest = false)
        {
            _smokeTest = smokeTest;
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = 800;
            _graphics.PreferredBackBufferHeight = 800;

            Content.RootDirectory = "Content";
            IsMouseVisible = true;
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
                Scene.Draw(_canvas.Canvas, 200, 200);
                _canvas.End();

                if (_smokeTest && ++_frameCount == 3)
                    CheckSmokeTestFrame();
            }

            base.Draw(gameTime);
        }

        /// <summary>
        /// The 200x200 canvas lands at the back buffer's origin, so its center is inside Scene's
        /// red circle and anything outside the canvas is still the black clear color.
        /// </summary>
        private void CheckSmokeTestFrame()
        {
            var inside = ReadBackBufferPixel(100, 100);
            var outside = ReadBackBufferPixel(400, 400);
            var passed = inside.R > 200 && inside.G < 50 && inside.B < 50 && outside == Color.Black;

            System.Console.WriteLine($"Smoke test {(passed ? "passed" : "FAILED")}: inside={inside}, outside={outside}");
            ExitCode = passed ? 0 : 1;
            Exit();
        }

        private Color ReadBackBufferPixel(int x, int y)
        {
            var pixel = new Color[1];
            GraphicsDevice.GetBackBufferData(new Rectangle(x, y, 1, 1), pixel, 0, 1);
            return pixel[0];
        }
    }
}
