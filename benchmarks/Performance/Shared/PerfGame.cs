using System.Diagnostics;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Performance
{
    /// <summary>One thing to measure: a scene drawn by one renderer.</summary>
    /// <param name="Kind"><c>engine</c> (the engine's own drawing) or <c>skia</c>.</param>
    /// <param name="Renderer">The column the result lands in, for example "SpriteBatch" or "Skia render target".</param>
    public sealed record PerfEntry(PerfScene Scene, string Kind, string Renderer);

    /// <summary>
    /// The loop shared by every performance app: an uncapped window (no vsync, no fixed timestep)
    /// that sweeps each <see cref="PerfEntry"/> through a warmup and a measured window, shows the
    /// last second's FPS in the title bar, and on completion writes a results table plus one
    /// screenshot per entry next to the exe, then exits. Subclasses only list their entries and draw them.
    ///
    /// Frame time is wall-clock time between successive Draw calls, so it covers everything the game
    /// pays for per frame (update, draw submission, Present and any GPU wait), not only the draw calls.
    /// The draw CPU column is the time spent inside the entry's draw code alone.
    ///
    /// Arguments: <c>--page slug</c> runs only that page's scenes; <c>--scene N</c> holds entry N
    /// (0-based, after the page filter) indefinitely instead of sweeping; <c>--no-screenshots</c> skips the PNGs;
    /// <c>--quick</c> shortens the warmup and measured windows for a smoke run.
    /// </summary>
    public abstract class PerfGame : Game
    {
        public const int Width = 1600;
        public const int Height = 900;

        // --quick shortens both windows for smoke runs; results from it are not for the docs.
        private readonly double WarmupSeconds;
        private readonly double MeasureSeconds;

        // Screenshots draw at this fixed animation time so every renderer's image is comparable.
        private const float ScreenshotTime = 1f;

        private readonly string _label;
        private readonly string? _page;
        private readonly int? _heldEntry;
        private readonly bool _screenshots;

        private PerfEntry[] _entries = null!;
        private int _entryIndex;
        private Phase _phase = Phase.Warmup;
        private double _phaseSeconds;
        private double _entrySeconds;
        private readonly List<double> _frameMs = new();
        private readonly List<double> _drawMs = new();
        private readonly List<string> _resultRows = new();

        private readonly Stopwatch _frameClock = new();
        private readonly Stopwatch _drawClock = new();
        private double _titleSeconds;
        private int _titleFrames;
        private string? _skipReason;
        private int _preparedEntry = -1;

        /// <summary>True once every entry has been measured and the results written.</summary>
        public bool Finished { get; private set; }

        private enum Phase { Warmup, Measure, Screenshot }

        protected PerfGame(string label, string[] args)
        {
            _label = label;
            int sceneArg = Array.IndexOf(args, "--scene");
            if (sceneArg >= 0 && sceneArg + 1 < args.Length)
                _heldEntry = int.Parse(args[sceneArg + 1]);
            int pageArg = Array.IndexOf(args, "--page");
            if (pageArg >= 0 && pageArg + 1 < args.Length)
                _page = args[pageArg + 1];
            _screenshots = !args.Contains("--no-screenshots");
            bool quick = args.Contains("--quick");
            WarmupSeconds = quick ? 0.3 : 2.0;
            MeasureSeconds = quick ? 0.5 : 4.0;

            new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = Width,
                PreferredBackBufferHeight = Height,
                SynchronizeWithVerticalRetrace = false,
                GraphicsProfile = GraphicsProfile.HiDef,
            };
            IsFixedTimeStep = false;
            IsMouseVisible = true;
            Window.Title = label;
        }

        protected override void LoadContent()
        {
            var scenes = PerfScene.BuildAll(Width, Height);
            if (_page != null)
                scenes = scenes.Where(s => s.Page == _page).ToArray();

            LoadRenderer();
            _entries = scenes.SelectMany(Entries).ToArray();
            _entryIndex = _heldEntry ?? 0;
        }

        /// <summary>Creates the renderer's GPU resources.</summary>
        protected abstract void LoadRenderer();

        /// <summary>The entries to measure for <paramref name="scene"/>, in order; none when this renderer has nothing to draw for it.</summary>
        protected abstract IEnumerable<PerfEntry> Entries(PerfScene scene);

        /// <summary>
        /// Draws <paramref name="entry"/> at animation time <paramref name="t"/> onto the back buffer.
        /// Throwing marks the entry as not supported here and moves on.
        /// </summary>
        protected abstract void DrawEntry(PerfEntry entry, float t);

        /// <summary>Called once per entry before the first draw, for work that must not count (building caches).</summary>
        protected virtual void PrepareEntry(PerfEntry entry) { }

        protected override void Draw(GameTime gameTime)
        {
            double frameMs = _frameClock.Elapsed.TotalMilliseconds;
            bool firstFrame = !_frameClock.IsRunning;
            _frameClock.Restart();
            if (!firstFrame)
                Advance(frameMs);

            if (_entryIndex >= _entries.Length)
            {
                base.Draw(gameTime);
                return;
            }

            var entry = _entries[_entryIndex];
            bool screenshot = _phase == Phase.Screenshot;

            try
            {
                if (_preparedEntry != _entryIndex)
                {
                    _preparedEntry = _entryIndex;
                    PrepareEntry(entry);
                }
                _drawClock.Restart();
                DrawEntry(entry, screenshot ? ScreenshotTime : (float)_entrySeconds);
                _drawClock.Stop();
                if (_phase == Phase.Measure && !firstFrame)
                    _drawMs.Add(_drawClock.Elapsed.TotalMilliseconds);
            }
            catch (Exception exception)
            {
                _skipReason = $"{exception.GetType().Name}: {exception.Message}".Replace('\n', ' ').Replace('|', '/');
                if (_skipReason.Length > 160)
                    _skipReason = _skipReason[..160];
                Console.Error.WriteLine($"[{_label}] {entry.Scene.Name} / {entry.Renderer}: {_skipReason}");
                RecordSkipped(entry, _skipReason);
                NextEntry();
                base.Draw(gameTime);
                return;
            }

            if (screenshot)
            {
                if (_screenshots && PerfEnvironment.Screenshots)
                    SaveScreenshot(entry);
                NextEntry();
            }

            base.Draw(gameTime);
        }

        private void Advance(double frameMs)
        {
            if (_entryIndex >= _entries.Length)
                return;

            double seconds = frameMs / 1000.0;
            _entrySeconds += seconds;
            _phaseSeconds += seconds;

            _titleSeconds += seconds;
            _titleFrames++;
            if (_titleSeconds >= 1.0)
            {
                double fps = _titleFrames / _titleSeconds;
                var entry = _entries[_entryIndex];
                Window.Title = $"{_label} | {entry.Scene.Name} | {entry.Renderer} | {fps:0} FPS ({1000.0 / fps:0.00} ms)";
                _titleSeconds = 0;
                _titleFrames = 0;
            }

            if (_heldEntry != null)
                return;

            if (_phase == Phase.Warmup && _phaseSeconds >= WarmupSeconds)
            {
                _phase = Phase.Measure;
                _phaseSeconds = 0;
                _frameMs.Clear();
                _drawMs.Clear();
            }
            else if (_phase == Phase.Measure)
            {
                _frameMs.Add(frameMs);
                if (_phaseSeconds >= MeasureSeconds)
                {
                    RecordResult(_entries[_entryIndex]);
                    _phase = Phase.Screenshot;
                }
            }
        }

        private void NextEntry()
        {
            _entryIndex++;
            _skipReason = null;
            if (_entryIndex >= _entries.Length)
            {
                WriteResults();
                Finished = true;
                // A page has no process to end; it reads Finished.
                if (PerfEnvironment.Publish == null)
                    Exit();
                return;
            }
            _phase = Phase.Warmup;
            _phaseSeconds = 0;
            _entrySeconds = 0;
        }

        private void RecordResult(PerfEntry entry)
        {
            _frameMs.Sort();
            _drawMs.Sort();
            double total = 0;
            foreach (var ms in _frameMs)
                total += ms;
            double fps = _frameMs.Count / (total / 1000.0);
            double median = Percentile(_frameMs, 0.5);
            double p95 = Percentile(_frameMs, 0.95);
            double draw = Percentile(_drawMs, 0.5);
            _resultRows.Add($"| {entry.Scene.Page} | {entry.Scene.Name} | {entry.Scene.Count} | {entry.Kind} | {entry.Renderer} | {fps:0} | {median:0.000} | {p95:0.000} | {draw:0.000} |  |");
        }

        private void RecordSkipped(PerfEntry entry, string reason) =>
            _resultRows.Add($"| {entry.Scene.Page} | {entry.Scene.Name} | {entry.Scene.Count} | {entry.Kind} | {entry.Renderer} | n/a |  |  |  | {reason} |");

        private static double Percentile(List<double> sorted, double p) =>
            sorted.Count == 0 ? 0 : sorted[(int)Math.Min(sorted.Count - 1, Math.Round(p * (sorted.Count - 1)))];

        /// <summary>Saves what is on the back buffer. Desktop apps write a PNG; platforms without a file system override this.</summary>
        protected virtual void SaveScreenshot(PerfEntry entry)
        {
            var data = new Color[Width * Height];
            GraphicsDevice.GetBackBufferData(data);
            using var texture = new Texture2D(GraphicsDevice, Width, Height);
            texture.SetData(data);

            string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            using var stream = File.Create(Path.Combine(dir, $"{Slug(_label)}-{Slug(entry.Scene.Name)}-{Slug(entry.Renderer)}.png"));
            texture.SaveAsPng(stream, Width, Height);
        }

        /// <summary>Platform and GPU lines for the results header.</summary>
        protected abstract string GpuDescription { get; }

        /// <summary>Writes the results. Desktop apps write a markdown file next to the exe; platforms without a file system override this.</summary>
        protected virtual void PublishResults(string fileName, string markdown)
        {
            if (PerfEnvironment.Publish != null)
                PerfEnvironment.Publish(fileName, markdown);
            else
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, fileName), markdown);
        }

        private void WriteResults()
        {
            var machine = PerfEnvironment.Machine?.Invoke()
                ?? new MachineDescription(MachineInfo.Os, MachineInfo.Cpu, MachineInfo.Power, MachineInfo.Commit);
            var sb = new StringBuilder();
            sb.AppendLine($"# {_label}");
            sb.AppendLine();
            sb.AppendLine($"- Date: {DateTime.Now:yyyy-MM-dd}");
            sb.AppendLine($"- Commit: {machine.Commit}");
            sb.AppendLine($"- OS: {machine.Os}");
            sb.AppendLine($"- CPU: {machine.Cpu}");
            sb.AppendLine($"- GPU: {GpuDescription}");
            sb.AppendLine($"- Power: {machine.Power}");
            sb.AppendLine($"- Window: {Width}x{Height}, vsync off, {MeasureSeconds:0}s measured per entry after {WarmupSeconds:0}s warmup");
            sb.AppendLine();
            sb.AppendLine("| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Note |");
            sb.AppendLine("|---|---|---:|---|---|---:|---:|---:|---:|---|");
            foreach (var row in _resultRows)
                sb.AppendLine(row);

            PublishResults($"perf-results-{Slug(_label)}.md", sb.ToString());
        }

        private static string Slug(string text) =>
            new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
    }
}
