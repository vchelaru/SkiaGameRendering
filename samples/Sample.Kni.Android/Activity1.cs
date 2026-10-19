using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Microsoft.Xna.Framework;
using SkiaGameRendering;
using SkiaGameRendering.Kni.Android;

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
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);
            var smoke = Intent?.GetBooleanExtra("smoke", false) ?? false;
            Game game = new LoggingGame(smoke);
            SetContentView((View)game.Services.GetService(typeof(View)));
            game.Run();
        }

        /// <summary>Logs the Skia backend once, then everything Game1 does is unchanged.</summary>
        sealed class LoggingGame : Game1
        {
            bool _logged;

            public LoggingGame(bool smokeTest) : base(smokeTest) { }

            protected override void Draw(GameTime gameTime)
            {
                base.Draw(gameTime);
                if (!_logged && SkiaRenderer.CurrentBackend is SkiaKniAndroidBackend backend)
                {
                    _logged = true;
                    Log.Info("SkiaSample", $"Skia backend: {backend.Description}");
                }
            }

            protected override void UnloadContent()
            {
                Log.Info("SkiaSample", $"Exit code {ExitCode}");
                base.UnloadContent();
            }
        }
    }
}
