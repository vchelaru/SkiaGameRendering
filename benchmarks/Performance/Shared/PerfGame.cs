using System.Diagnostics;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Performance
{
    /// <summary>
    /// The loop shared by every performance app: an uncapped window (no vsync, no fixed timestep)
    /// that sweeps each <see cref="SpriteScene"/> through a warmup and a measured window, shows the
    /// last second's FPS in the title bar, and on completion writes a results table plus one
    /// screenshot per scene next to the exe, then exits. Subclasses only draw the scene.
    ///
    /// Frame time is wall-clock time between successive Draw calls, so it covers everything the game
    /// pays for per frame (update, draw submission, Present and any GPU wait), not only the draw calls.
    ///
    /// Arguments: <c>--scene N</c> holds scene N (0-based) indefinitely instead of sweeping.
    /// </summary>
    public abstract class PerfGame : Game
    {
        public const int Width = 1600;
        public const int Height = 900;

        private const double WarmupSeconds = 2.0;
        private const double MeasureSeconds = 5.0;

        // Screenshots draw at this fixed animation time so the Raw and Skia images are comparable.
        private const float ScreenshotTime = 1f;

        private readonly string _label;
        private readonly int? _heldScene;

        private SpriteScene[] _scenes = null!;
        private int _sceneIndex;
        private Phase _phase = Phase.Warmup;
        private double _phaseSeconds;
        private double _sceneSeconds;
        private readonly List<double> _frameMs = new();
        private readonly List<string> _resultRows = new();

        private readonly Stopwatch _frameClock = new();
        private double _titleSeconds;
        private int _titleFrames;

        private enum Phase { Warmup, Measure, Screenshot }

        protected PerfGame(string label, string[] args)
        {
            _label = label;
            int sceneArg = Array.IndexOf(args, "--scene");
            if (sceneArg >= 0 && sceneArg + 1 < args.Length)
                _heldScene = int.Parse(args[sceneArg + 1]);

            new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = Width,
                PreferredBackBufferHeight = Height,
                SynchronizeWithVerticalRetrace = false,
            };
            IsFixedTimeStep = false;
            IsMouseVisible = true;
            Window.Title = label;
        }

        protected override void LoadContent()
        {
            _scenes = SpriteScene.BuildAll(Width, Height);
            _sceneIndex = _heldScene ?? 0;
            LoadRenderer();
        }

        protected abstract void LoadRenderer();

        /// <summary>Draws <paramref name="scene"/> at animation time <paramref name="t"/> onto the back buffer.</summary>
        protected abstract void DrawScene(SpriteScene scene, float t);

        protected override void Draw(GameTime gameTime)
        {
            double frameMs = _frameClock.Elapsed.TotalMilliseconds;
            bool firstFrame = !_frameClock.IsRunning;
            _frameClock.Restart();
            if (!firstFrame)
                Advance(frameMs);

            var scene = _scenes[_sceneIndex];
            bool screenshot = _phase == Phase.Screenshot;
            DrawScene(scene, screenshot ? ScreenshotTime : (float)_sceneSeconds);

            if (screenshot)
            {
                SaveScreenshot(scene);
                NextScene();
            }

            base.Draw(gameTime);
        }

        private void Advance(double frameMs)
        {
            double seconds = frameMs / 1000.0;
            _sceneSeconds += seconds;
            _phaseSeconds += seconds;

            _titleSeconds += seconds;
            _titleFrames++;
            if (_titleSeconds >= 1.0)
            {
                double fps = _titleFrames / _titleSeconds;
                Window.Title = $"{_label} | {_scenes[_sceneIndex].Name} | {fps:0} FPS ({1000.0 / fps:0.00} ms)";
                _titleSeconds = 0;
                _titleFrames = 0;
            }

            if (_heldScene != null)
                return;

            if (_phase == Phase.Warmup && _phaseSeconds >= WarmupSeconds)
            {
                _phase = Phase.Measure;
                _phaseSeconds = 0;
                _frameMs.Clear();
            }
            else if (_phase == Phase.Measure)
            {
                _frameMs.Add(frameMs);
                if (_phaseSeconds >= MeasureSeconds)
                {
                    RecordResult(_scenes[_sceneIndex]);
                    _phase = Phase.Screenshot;
                }
            }
        }

        private void NextScene()
        {
            _sceneIndex++;
            if (_sceneIndex >= _scenes.Length)
            {
                WriteResults();
                Exit();
                _sceneIndex = _scenes.Length - 1;
                return;
            }
            _phase = Phase.Warmup;
            _phaseSeconds = 0;
            _sceneSeconds = 0;
        }

        private void RecordResult(SpriteScene scene)
        {
            _frameMs.Sort();
            double total = 0;
            foreach (var ms in _frameMs)
                total += ms;
            double fps = _frameMs.Count / (total / 1000.0);
            double median = Percentile(0.5);
            double p95 = Percentile(0.95);
            _resultRows.Add($"| {scene.Name} | {scene.Count} | {fps:0} | {median:0.000} | {p95:0.000} |");
        }

        private double Percentile(double p) =>
            _frameMs.Count == 0 ? 0 : _frameMs[(int)Math.Min(_frameMs.Count - 1, Math.Round(p * (_frameMs.Count - 1)))];

        private void SaveScreenshot(SpriteScene scene)
        {
            var data = new Color[Width * Height];
            GraphicsDevice.GetBackBufferData(data);
            using var texture = new Texture2D(GraphicsDevice, Width, Height);
            texture.SetData(data);

            string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            using var stream = File.Create(Path.Combine(dir, $"{Slug(_label)}-{Slug(scene.Name)}.png"));
            texture.SaveAsPng(stream, Width, Height);
        }

        private void WriteResults()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {_label}");
            sb.AppendLine();
            sb.AppendLine($"- Date: {DateTime.Now:yyyy-MM-dd}");
            sb.AppendLine($"- Commit: {MachineInfo.Commit}");
            sb.AppendLine($"- OS: {MachineInfo.Os}");
            sb.AppendLine($"- CPU: {MachineInfo.Cpu}");
            sb.AppendLine($"- GPU: {GpuInfo.Query(GraphicsDevice)}");
            sb.AppendLine($"- Power: {MachineInfo.Power}");
            sb.AppendLine($"- Window: {Width}x{Height}, vsync off, {MeasureSeconds:0}s measured per scene after {WarmupSeconds:0}s warmup");
            sb.AppendLine();
            sb.AppendLine("| Scene | Sprites | FPS | Median frame ms | p95 frame ms |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            foreach (var row in _resultRows)
                sb.AppendLine(row);

            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, $"perf-results-{Slug(_label)}.md"), sb.ToString());
        }

        private static string Slug(string text) =>
            new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
    }
}
