using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SampleShared;
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

        /// <summary>An extra pass/fail check a platform's Program adds to the smoke test; runs just before the exit code is set.</summary>
        public System.Func<GraphicsDevice, bool> ExtraSmokeCheck { get; set; }

        /// <summary>
        /// When set, saves the Scene to this PNG path, with a transparent background, and exits.
        /// </summary>
        public string ScreenshotPath { get; init; }
        private bool _screenshotSaved;

        // The canvas is always the Scene's fixed size; the 800x800 window shows it 1:1.
        // Behind the Scene, in every sample (see Scene.Background).
        private static readonly Color ClearColor = new Color(Scene.Background.Red, Scene.Background.Green, Scene.Background.Blue);
        private static int CanvasWidth => Scene.Width;
        private static int CanvasHeight => Scene.Height;

        public Game1(bool smokeTest = false)
        {
            _smokeTest = smokeTest;
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = 800;
            _graphics.PreferredBackBufferHeight = 800;

            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        /// <summary>Leaves the app; a platform that forbids <see cref="Game.Exit"/> (KNI Android, iOS) overrides it.</summary>
#if __IOS__
        // MonoGame iOS makes Game.Exit a compile error.
        protected virtual void ExitGame() { }
#else
        protected virtual void ExitGame() => Exit();
#endif

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                ExitGame();

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
                SkiaRenderer.Initialize(GraphicsDevice);

            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(ClearColor);

            if (SkiaRenderer.IsInitialized)
            {
                // The smoke test disposes a target before drawing with another, which crashed DesktopVK.
                if (_canvas == null && _smokeTest)
                    new SkiaRenderTarget2D(GraphicsDevice, 64, 64).Dispose();
                _canvas ??= new SkiaRenderTarget2D(GraphicsDevice, CanvasWidth, CanvasHeight);
                _canvas.Begin();
                Scene.Draw(_canvas.Canvas);
                if (ScreenshotPath != null && ++_frameCount == 3)
                    SaveScreenshot();
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
                if (_screenshotSaved)
                    SaveBackBufferScreenshot();

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
        /// The 800x800 canvas fills the 800x800 back buffer. Scene's four 400x400 cells hold the red circle,
        /// the blue SVG water drop (which proves the SVG parser survived trimming), the gradient and the logo
        /// PNG (opaque gray text, transparent margin); the transparent part shows the clear color.
        /// </summary>
        private void CheckSmokeTestFrame()
        {
            var passed = CheckScene(ReadBackBufferPixel, ClearColor, out var text);

            System.Console.WriteLine($"Smoke test {(passed ? "passed" : "FAILED")}: {text}");
            ExitCode = passed && !_leakFailed && (ExtraSmokeCheck?.Invoke(GraphicsDevice) ?? true) ? 0 : 1;
            ExitGame();
        }

        /// <summary>The shared check of the Scene's four cell centers and a transparent pixel, against <paramref name="background"/>.</summary>
        private static bool CheckScene(System.Func<int, int, Color> read, Color background, out string text)
        {
            var circle = read(Scene.CellCenter, Scene.CellCenter);
            var drop = read(Scene.Cell + Scene.CellCenter, Scene.CellCenter);
            var gradient = read(Scene.CellCenter, Scene.Cell + Scene.CellCenter);
            var logo = read(Scene.LogoOpaqueX, Scene.LogoOpaqueY);
            var margin = read(Scene.LogoTransparentX, Scene.LogoTransparentY);
            text = $"circle={circle}, drop={drop}, gradient={gradient}, logo={logo}, margin={margin}";
            return circle.R > 200 && circle.G < 50 && circle.B < 50
                && drop.R < 100 && drop.B > 150
                && gradient.R > 120 && gradient.G < 80 && gradient.B > 100
                && logo.R > 70 && logo.R < 150 && logo.G > 70 && logo.G < 150 && logo.B > 70 && logo.B < 150 && logo.A > 200
                && margin == background;
        }

        /// <summary>
        /// MonoGame's native Vulkan platform hangs the next frame after GetBackBufferData on Mesa's
        /// lavapipe, so CI sets SKIAGAMERENDERING_SMOKE_SKIA_READBACK=1 there and checks the pixels Skia
        /// itself drew instead. That proves the Skia draw, not MonoGame's composite onto the back buffer.
        /// </summary>
        private void CheckSkiaSurface()
        {
            _skiaChecked = true;
            var passed = CheckScene(ReadSkiaPixel, Color.Transparent, out var text);

            System.Console.WriteLine($"Smoke test {(passed ? "passed" : "FAILED")} (Skia surface readback): {text}");
            ExitCode = passed && !_leakFailed && (ExtraSmokeCheck?.Invoke(GraphicsDevice) ?? true) ? 0 : 1;
            ExitGame();
        }

        private void SaveScreenshot()
        {
            using var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(CanvasWidth, CanvasHeight, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
            if (!_canvas!.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
                throw new System.InvalidOperationException("Skia surface readback failed.");
            using var stream = System.IO.File.Create(ScreenshotPath);
            bitmap.Encode(stream, SkiaSharp.SKEncodedImageFormat.Png, 100);
            _screenshotSaved = true;
        }

        /// <summary>Saves the back buffer after the composite (the final frame) next to the Skia screenshot, as "-backbuffer.png".</summary>
        private void SaveBackBufferScreenshot()
        {
            var pixels = new Color[CanvasWidth * CanvasHeight];
            GraphicsDevice.GetBackBufferData(new Rectangle(0, 0, CanvasWidth, CanvasHeight), pixels, 0, pixels.Length);
            using var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(CanvasWidth, CanvasHeight, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul));
            var rgba = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                rgba[i * 4] = pixels[i].R;
                rgba[i * 4 + 1] = pixels[i].G;
                rgba[i * 4 + 2] = pixels[i].B;
                rgba[i * 4 + 3] = pixels[i].A;
            }
            System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
            using var stream = System.IO.File.Create(System.IO.Path.ChangeExtension(ScreenshotPath, null) + "-backbuffer.png");
            bitmap.Encode(stream, SkiaSharp.SKEncodedImageFormat.Png, 100);
            ExitGame();
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
