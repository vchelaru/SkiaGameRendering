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
        private bool _skiaChecked;
        private bool _leakFailed;

        // SKIAGAMERENDERING_SMOKE_LEAK_FRAMES=N makes --smoke-test run LeakWarmupFrames + 2N frames, compare
        // the two N-frame windows for growth, and only then check the pixels.
        private const int LeakWarmupFrames = 50;
        private static readonly int LeakFrames =
            int.TryParse(System.Environment.GetEnvironmentVariable("SKIAGAMERENDERING_SMOKE_LEAK_FRAMES"), out var n) && n > 0 ? n : 0;
        private (long Private, long Managed, int Handles) _leakStart, _leakMiddle;

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
                var checkFrame = false;
                if (_smokeTest)
                {
                    _frameCount++;
                    if (LeakFrames > 0)
                        SampleLeak();
                    checkFrame = _frameCount == (LeakFrames > 0 ? LeakWarmupFrames + 2 * LeakFrames : 3);
                }
                // Mid-draw, so the readback sees the surface in the state Skia expects.
                var readback = System.Environment.GetEnvironmentVariable("SKIAGAMERENDERING_SMOKE_SKIA_READBACK");
                if (checkFrame && readback == "1")
                    CheckSkiaSurface();
                _canvas.End();

                if (checkFrame && !_skiaChecked)
                    CheckSmokeTestFrame();
            }

            base.Draw(gameTime);
        }

        private void SampleLeak()
        {
            if (_frameCount != LeakWarmupFrames && _frameCount != LeakWarmupFrames + LeakFrames && _frameCount != LeakWarmupFrames + 2 * LeakFrames)
                return;

            // Undisposed Skia wrappers free their native memory from finalizers, so settle those first.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var now = (process.PrivateMemorySize64, System.GC.GetTotalMemory(true), process.HandleCount);

            if (_frameCount == LeakWarmupFrames)
                _leakStart = now;
            else if (_frameCount == LeakWarmupFrames + LeakFrames)
                _leakMiddle = now;
            else
            {
                const long mb = 1024 * 1024;
                System.Console.WriteLine($"Leak check over {2 * LeakFrames} frames: private {_leakStart.Private / mb} -> {_leakMiddle.Private / mb} -> {now.Item1 / mb} MB, " +
                    $"managed {_leakStart.Managed / mb} -> {_leakMiddle.Managed / mb} -> {now.Item2 / mb} MB, handles {_leakStart.Handles} -> {_leakMiddle.Handles} -> {now.Item3}");
                // Same loose limits as tests/Shared/FrameLeakCheck.cs: they catch a texture or command list per frame.
                _leakFailed = now.Item1 - _leakStart.Private >= 24 * mb || now.Item1 - _leakMiddle.Private >= 12 * mb
                    || now.Item2 - _leakStart.Managed >= 2 * mb || now.Item3 - _leakStart.Handles >= 32;
                if (_leakFailed)
                    System.Console.WriteLine("Leak check FAILED: a resource grew with the frame count.");
            }
        }

        protected override void UnloadContent()
        {
            _canvas?.Dispose();
            _canvas = null;
            SkiaRenderer.Dispose();
            base.UnloadContent();
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
            ExitCode = passed && !_leakFailed ? 0 : 1;
            Exit();
        }

        /// <summary>
        /// MonoGame's native Vulkan platform hangs the next frame after GetBackBufferData on Mesa's
        /// lavapipe, so CI sets SKIAGAMERENDERING_SMOKE_SKIA_READBACK=1 there and checks the pixels Skia
        /// itself drew instead. That proves the Skia draw, not MonoGame's composite onto the back buffer.
        /// </summary>
        private void CheckSkiaSurface()
        {
            _skiaChecked = true;
            var circle = ReadSkiaPixel(50, 50);
            var drop = ReadSkiaPixel(150, 50);
            var passed = circle.R > 200 && circle.G < 50 && circle.B < 50
                && drop.R < 100 && drop.B > 150;

            System.Console.WriteLine($"Smoke test {(passed ? "passed" : "FAILED")} (Skia surface readback): circle={circle}, drop={drop}");
            ExitCode = passed && !_leakFailed ? 0 : 1;
            Exit();
        }

        private Color ReadSkiaPixel(int x, int y)
        {
            using var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(1, 1, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
            if (!_canvas!.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, x, y))
                return Color.Transparent;
            var p = bitmap.GetPixelSpan();
            return new Color(p[0], p[1], p[2], p[3]);
        }

        private Color ReadBackBufferPixel(int x, int y)
        {
            var pixel = new Color[1];
            GraphicsDevice.GetBackBufferData(new Rectangle(x, y, 1, 1), pixel, 0, 1);
            return pixel[0];
        }
    }
}
