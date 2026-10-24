using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Microsoft.Xna.Framework;
using Sample.Shared;
using SkiaGameRendering;

namespace Sample
{
    /// <summary>
    /// Launch with <c>adb shell am start -n com.vchelaru.skiagamerendering.sample/crc6...Activity1 --ez smoke true</c>
    /// to run the shared smoke test: the activity logs the verdict under the "SkiaSample" tag and finishes.
    /// </summary>
    [Activity(Label = "Skia Sample",
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
            _game.ExtraSmokeCheck = graphicsDevice => MobileDirectCanvasCheck.Run(graphicsDevice, message => Log.Info("SkiaSample", message));
            SetContentView((View)_game.Services.GetService(typeof(View)));
            _game.Run();
        }

        // The process outlives the activity, and the next launch builds a new GraphicsDevice, which
        // SkiaRenderer rejects while it is still bound to the old one. KNI does not unload content
        // when an activity is destroyed, so release Skia here.
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

            // KNI Android throws on Game.Exit (the platform does not allow closing programmatically).
            protected override void ExitGame()
            {
                Log.Info("SkiaSample", $"Exit code {ExitCode}");
                _activity.Finish();
            }

            protected override void Draw(GameTime gameTime)
            {
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
