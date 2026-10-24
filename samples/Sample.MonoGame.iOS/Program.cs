using System.Runtime.InteropServices;
using Foundation;
using Microsoft.Xna.Framework.Graphics;
using Sample.Shared;
using SkiaGameRendering;
using Tests.Shared;
using UIKit;

namespace Sample
{
    /// <summary>
    /// <c>xcrun simctl launch &lt;device&gt; com.vchelaru.skiagamerendering.monogame.sample --smoke-test</c> runs
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
        private const int GL_FRAMEBUFFER_BINDING = 0x8CA6;

        [DllImport("/System/Library/Frameworks/OpenGLES.framework/OpenGLES")]
        private static extern void glGetIntegerv(int pname, out int data);

        readonly bool _smokeTest;
        bool _logged;

        public IosGame(bool smokeTest) : base(smokeTest)
        {
            _smokeTest = smokeTest;
            if (!smokeTest)
                return;

            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "smoke-test.log");
            Console.SetOut(new StreamWriter(path, append: false) { AutoFlush = true });
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.WriteLine($"Unhandled exception: {e.ExceptionObject}");
            // Unbinding a multisampled render target throws a NullReferenceException inside MonoGame's own resolve
            // (GraphicsDevice.FramebufferHelper.BlitFramebuffer), as on Android, so the check skips 4x MSAA.
            ExtraSmokeCheck = graphicsDevice =>
                MobileDirectCanvasCheck.Run(graphicsDevice, Console.WriteLine,
                    msaaSkipReason: "MonoGame cannot resolve a multisampled target on GLES")
                & BackBufferAfterRebuild(graphicsDevice);
        }

        /// <summary>
        /// MonoGame iOS deletes its back buffer's FBO and renderbuffer and makes new ones on every view
        /// layout, usually under the same GL names. SkiaDirectCanvas must draw into the new renderbuffer.
        /// </summary>
        bool BackBufferAfterRebuild(GraphicsDevice graphicsDevice)
        {
            graphicsDevice.SetRenderTarget(null);
            glGetIntegerv(GL_FRAMEBUFFER_BINDING, out var before);
            var controller = (UIViewController)Services.GetService(typeof(UIViewController));
            controller.View!.LayoutSubviews();
            graphicsDevice.SetRenderTarget(null);
            glGetIntegerv(GL_FRAMEBUFFER_BINDING, out var after);

            var errors = DirectDrawLayering.BackBuffer(graphicsDevice);
            Console.WriteLine($"Direct canvas, back buffer after a rebuild (FBO {before} -> {after}): " +
                (errors == "" ? "passed" : "FAILED: " + errors));
            return errors == "";
        }

        // MonoGame's Game.Exit does nothing on iOS.
        protected override void ExitGame()
        {
            if (!_smokeTest)
                return;
            Console.WriteLine($"Exit code {ExitCode}");
            Console.Out.Flush();
            Environment.Exit(ExitCode);
        }

        protected override void Draw(Microsoft.Xna.Framework.GameTime gameTime)
        {
            // Game1 asks for an 800x800 back buffer; the smoke test reads pixels from the full-screen one.
            var parameters = GraphicsDevice.PresentationParameters;
            GraphicsDevice.Viewport = new Viewport(0, 0, parameters.BackBufferWidth, parameters.BackBufferHeight);

            base.Draw(gameTime);
            if (!_logged && SkiaRenderer.CurrentBackend is SkiaIosBackend backend)
            {
                _logged = true;
                Console.WriteLine($"Skia backend: {backend.Description}, back buffer {parameters.BackBufferWidth}x{parameters.BackBufferHeight}");
            }
        }
    }
}
