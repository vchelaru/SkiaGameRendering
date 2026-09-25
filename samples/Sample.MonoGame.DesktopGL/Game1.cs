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
        /// The 200x200 canvas lands at the back buffer's origin. Scene's first two 100x100 cells hold
        /// the red circle and the blue SVG water drop (which proves the SVG parser survived trimming);
        /// anything outside the canvas is still the black clear color.
        /// </summary>
        private void CheckSmokeTestFrame()
        {
            var circle = ReadBackBufferPixel(50, 50);
            var drop = ReadBackBufferPixel(150, 50);
            var outside = ReadBackBufferPixel(400, 400);
            var passed = circle.R > 200 && circle.G < 50 && circle.B < 50
                && drop.R < 100 && drop.B > 150
                && outside == Color.Black;

            System.Console.WriteLine($"Smoke test {(passed ? "passed" : "FAILED")}: circle={circle}, drop={drop}, outside={outside}");
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
