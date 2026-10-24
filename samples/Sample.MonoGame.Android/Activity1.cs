using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Sample.Shared;
using SkiaGameRendering;

namespace Sample
{
    /// <summary>
    /// Launch with <c>adb shell am start -n com.vchelaru.skiagamerendering.monogame.sample/crc6...Activity1 --ez smoke true</c>
    /// to run the shared smoke test: the activity logs the verdict under the "SkiaSample" tag and finishes.
    /// </summary>
    [Activity(Label = "Skia MonoGame Sample",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleInstance,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
            ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode | ConfigChanges.SmallestScreenSize,
        ScreenOrientation = ScreenOrientation.FullSensor)]
    public class Activity1 : AndroidGameActivity
    {
        LoggingGame _game;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            var smoke = Intent?.GetBooleanExtra("smoke", false) ?? false;
            _game = new LoggingGame(this, smoke);
            // Unbinding a multisampled render target throws a NullReferenceException inside MonoGame's own
            // resolve (GraphicsDevice.FramebufferHelper.BlitFramebuffer) on GLES, so the check skips 4x MSAA.
            _game.ExtraSmokeCheck = graphicsDevice => MobileDirectCanvasCheck.Run(graphicsDevice, message => Log.Info("SkiaSample", message),
                msaaSkipReason: "MonoGame cannot resolve a multisampled target on GLES");
            SetContentView((View)_game.Services.GetService(typeof(View)));
            _game.Run();
        }

        // The process outlives the activity, and the next launch builds a new GraphicsDevice, which
        // SkiaRenderer rejects while it is still bound to the old one. Release Skia here.
        protected override void OnDestroy()
        {
            Log.Info("SkiaSample", "OnDestroy");
            _game?.ReleaseSkia();
            _game = null;
            base.OnDestroy();
        }

        /// <summary>Logs the Skia backend once, then everything Game1 does is unchanged.</summary>
        sealed class LoggingGame : Game1
        {
            readonly Activity _activity;
            bool _logged;

            public LoggingGame(Activity activity, bool smokeTest) : base(smokeTest)
            {
                _activity = activity;
            }

            public void ReleaseSkia() => UnloadContent();

            // MonoGame's Game.Exit on Android only moves the task to the back; finish the activity instead.
            protected override void ExitGame()
            {
                Log.Info("SkiaSample", $"Exit code {ExitCode}");
                _activity.Finish();
            }

            protected override void Draw(GameTime gameTime)
            {
                // Game1 asks for a square back buffer, so MonoGame letterboxes it with a viewport offset into
                // the taller window. GetBackBufferData ignores that offset (and MonoGame drops it at the first
                // render target switch), so the smoke test draws without it.
                var parameters = GraphicsDevice.PresentationParameters;
                GraphicsDevice.Viewport = new Viewport(0, 0, parameters.BackBufferWidth, parameters.BackBufferHeight);

                base.Draw(gameTime);
                if (!_logged && SkiaRenderer.CurrentBackend is SkiaAndroidBackend backend)
                {
                    _logged = true;
                    Log.Info("SkiaSample", $"Skia backend: {backend.Description}");
                }
            }
        }
    }
}
