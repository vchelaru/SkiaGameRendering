using Foundation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sample.Shared;
using SkiaGameRendering;
using Tests.Shared;
using UIKit;

namespace Sample
{
    /// <summary>
    /// <c>xcrun simctl launch &lt;device&gt; com.vchelaru.skiagamerendering.sample --smoke-test</c> runs
    /// the shared smoke test: it writes its log to <c>Documents/smoke-test.log</c> in the app's data
    /// container, ending with "Exit code N", and exits. <c>eng/run-ios-smoke-test.sh</c> does all of it.
    /// </summary>
    [Register("AppDelegate")]
    class Program : UIApplicationDelegate
    {
        static IosGame _game;

        static void Main(string[] args) => UIApplication.Main(args, null, typeof(Program));

        public override void FinishedLaunching(UIApplication application)
        {
            var smokeTest = NSProcessInfo.ProcessInfo.Arguments.Contains("--smoke-test");
            _game = new IosGame(smokeTest);
            _game.Run();
        }
    }

    /// <summary>Game1 plus the iOS smoke test's log file, exit, and back-buffer rebuild check.</summary>
    sealed class IosGame : Game1
    {
        readonly bool _smokeTest;
        bool _logged;

        public IosGame(bool smokeTest) : base(smokeTest)
        {
            _smokeTest = smokeTest;

            // KNI 4.3.9001's iOS GraphicsDeviceManager never fills in PresentationParameters.DeviceWindowHandle,
            // and its graphics context constructor looks the game window up by it, so creating the device
            // throws a KeyNotFoundException for any app. Setting it here is the whole workaround.
            var manager = (GraphicsDeviceManager)Services.GetService(typeof(IGraphicsDeviceManager));
            manager.PreparingDeviceSettings += (_, e) => e.GraphicsDeviceInformation.PresentationParameters.DeviceWindowHandle = Window.Handle;
            if (!smokeTest)
                return;

            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "smoke-test.log");
            Console.SetOut(new StreamWriter(path, append: false) { AutoFlush = true });
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.WriteLine($"Unhandled exception: {e.ExceptionObject}");
            ExtraSmokeCheck = graphicsDevice =>
                MobileDirectCanvasCheck.Run(graphicsDevice, Console.WriteLine)
                & BackBufferAfterRebuild(graphicsDevice);
        }

        /// <summary>
        /// KNI iOS deletes its back buffer's FBO and renderbuffer and makes new ones on every view
        /// layout. The rebuild must succeed after SkiaDirectCanvas drew to the old renderbuffer, and
        /// SkiaDirectCanvas must then draw into the new one.
        /// </summary>
        bool BackBufferAfterRebuild(GraphicsDevice graphicsDevice)
        {
            var controller = (UIViewController)Services.GetService(typeof(UIViewController));
            controller.View!.LayoutSubviews();

            var errors = DirectDrawLayering.BackBuffer(graphicsDevice);
            Console.WriteLine($"Direct canvas, back buffer after a rebuild: " +
                (errors == "" ? "passed" : "FAILED: " + errors));
            return errors == "";
        }

        // Game.Exit does nothing on iOS.
        protected override void ExitGame()
        {
            if (!_smokeTest)
                return;
            Console.WriteLine($"Exit code {ExitCode}");
            Console.Out.Flush();
            Environment.Exit(ExitCode);
        }

        protected override void Draw(GameTime gameTime)
        {
            var parameters = GraphicsDevice.PresentationParameters;
            base.Draw(gameTime);
            if (!_logged && SkiaRenderer.CurrentBackend is SkiaIosBackend backend)
            {
                _logged = true;
                Console.WriteLine($"Skia backend: {backend.Description}, back buffer {parameters.BackBufferWidth}x{parameters.BackBufferHeight}");
            }
        }
    }
}
