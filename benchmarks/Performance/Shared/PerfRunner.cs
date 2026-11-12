using System.Diagnostics;
using System.Text;

namespace Performance
{
    /// <summary>One thing to measure: a scene drawn by one renderer.</summary>
    /// <param name="Kind"><c>engine</c> (the engine's own drawing) or <c>skia</c>.</param>
    /// <param name="Renderer">The column the result lands in, for example "SpriteBatch" or "Skia render target".</param>
    public sealed record PerfEntry(PerfScene Scene, string Kind, string Renderer);

    /// <summary>What a <see cref="PerfRunner"/> needs from the engine that owns the window and the frame loop.</summary>
    public interface IPerfHost
    {
        /// <summary>Called once per entry before the first draw, for work that must not count (building caches).</summary>
        void PrepareEntry(PerfEntry entry);

        /// <summary>
        /// Draws <paramref name="entry"/> at animation time <paramref name="t"/> onto the window.
        /// Throwing marks the entry as not supported here and moves on.
        /// </summary>
        void DrawEntry(PerfEntry entry, float t);

        /// <summary>Saves what <see cref="DrawEntry"/> just drew as a PNG at <paramref name="path"/>.</summary>
        void SaveScreenshot(PerfEntry entry, string path);

        /// <summary>False while the window is in the background.</summary>
        bool IsActive { get; }

        void SetTitle(string title);

        /// <summary>Platform and GPU line for the results header.</summary>
        string GpuDescription { get; }

        /// <summary>The MSAA line for the results header: what was requested and what the host got.</summary>
        string MsaaDescription { get; }
    }

    /// <summary>
    /// The measurement loop shared by every performance app, with no engine in it: it sweeps each
    /// <see cref="PerfEntry"/> through a warmup and a measured window, shows the last second's FPS in the title
    /// bar, and on completion writes a results table plus one screenshot per entry next to the exe. The host
    /// owns the window and calls <see cref="Frame"/> once per frame, uncapped (no vsync, no fixed timestep).
    ///
    /// Frame time is wall-clock time between successive <see cref="Frame"/> calls, so it covers everything the
    /// game pays for per frame (update, draw submission, Present and any GPU wait), not only the draw calls.
    /// The draw CPU column is the time spent inside the entry's draw code alone.
    /// </summary>
    public sealed class PerfRunner
    {
        public const int Width = 1600;
        public const int Height = 900;

        // Screenshots draw at this fixed animation time so every renderer's image is comparable.
        private const float ScreenshotTime = 1f;

        private readonly string _label;
        private readonly PerfOptions _options;
        private readonly IPerfHost _host;

        private PerfEntry[] _entries = Array.Empty<PerfEntry>();
        private int _entryIndex;
        private Phase _phase = Phase.Warmup;
        private double _phaseSeconds;
        private double _entrySeconds;
        private readonly List<double> _frameMs = new();
        private readonly List<double> _drawMs = new();
        private readonly List<string> _resultRows = new();
        private int _inactiveFrames;
        private bool _anyInactive;

        private readonly Stopwatch _frameClock = new();
        private readonly Stopwatch _drawClock = new();
        private double _titleSeconds;
        private int _titleFrames;
        private int _preparedEntry = -1;

        /// <summary>True once every entry has been measured and the results written.</summary>
        public bool Finished { get; private set; }

        private enum Phase { Warmup, Measure, Screenshot }

        public PerfRunner(string label, PerfOptions options, IPerfHost host)
        {
            _label = label;
            _options = options;
            _host = host;
            host.SetTitle(label);
        }

        public PerfOptions Options => _options;

        /// <summary>Builds the scenes, keeps the requested pages and lists each one's entries. Call once the renderer exists.</summary>
        public void Start(Func<PerfScene, IEnumerable<PerfEntry>> entries)
        {
            var all = PerfScene.BuildAll(Width, Height);
            var scenes = all;
            if (_options.Pages != null)
                scenes = scenes.Where(s => _options.Pages.Contains(s.Page)).ToArray();
            if (_options.SceneNames != null)
            {
                var unknown = _options.SceneNames
                    .Where(n => !all.Any(s => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (unknown.Length > 0)
                    throw new ArgumentException($"Unknown --scenes name(s): {string.Join(", ", unknown)}. Valid names: "
                        + string.Join(", ", all.Select(s => s.Name)) + ".");
                scenes = scenes.Where(s => _options.SceneNames.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            }
            _entries = scenes.SelectMany(entries).ToArray();
            _entryIndex = _options.HeldEntry ?? 0;
            if (_entryIndex >= _entries.Length)
                Finish();
        }

        /// <summary>Measures the frame that just ended and draws the next one.</summary>
        public void Frame()
        {
            double frameMs = _frameClock.Elapsed.TotalMilliseconds;
            bool firstFrame = !_frameClock.IsRunning;
            _frameClock.Restart();
            if (!firstFrame)
                Advance(frameMs);

            if (_entryIndex >= _entries.Length)
                return;

            var entry = _entries[_entryIndex];
            bool screenshot = _phase == Phase.Screenshot;

            try
            {
                if (_preparedEntry != _entryIndex)
                {
                    _preparedEntry = _entryIndex;
                    _host.PrepareEntry(entry);
                }
                _drawClock.Restart();
                _host.DrawEntry(entry, screenshot ? ScreenshotTime : (float)_entrySeconds);
                _drawClock.Stop();
                if (_phase == Phase.Measure && !firstFrame)
                    _drawMs.Add(_drawClock.Elapsed.TotalMilliseconds);
            }
            catch (Exception exception)
            {
                string reason = $"{exception.GetType().Name}: {exception.Message}".Replace('\n', ' ').Replace('|', '/');
                if (reason.Length > 160)
                    reason = reason[..160];
                Console.Error.WriteLine($"[{_label}] {entry.Scene.Name} / {entry.Renderer}: {reason}");
                RecordSkipped(entry, reason);
                NextEntry();
                return;
            }

            if (screenshot)
            {
                if (_options.Screenshots && PerfEnvironment.Screenshots)
                {
                    string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
                    Directory.CreateDirectory(dir);
                    _host.SaveScreenshot(entry, Path.Combine(dir, $"{Slug(_label)}-{Slug(entry.Scene.Name)}-{Slug(entry.Renderer)}.png"));
                }
                NextEntry();
            }
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
                _host.SetTitle($"{_label} | {entry.Scene.Name} | {entry.Renderer} | {fps:0} FPS ({1000.0 / fps:0.00} ms)");
                _titleSeconds = 0;
                _titleFrames = 0;
            }

            if (_options.HeldEntry != null)
                return;

            if (_phase == Phase.Warmup && _phaseSeconds >= _options.WarmupSeconds)
            {
                _phase = Phase.Measure;
                _phaseSeconds = 0;
                _frameMs.Clear();
                _drawMs.Clear();
                _inactiveFrames = 0;
            }
            else if (_phase == Phase.Measure)
            {
                _frameMs.Add(frameMs);
                // The OS or driver can still throttle a background window, so record it.
                if (!_host.IsActive)
                    _inactiveFrames++;
                if (_phaseSeconds >= _options.MeasureSeconds)
                {
                    RecordResult(_entries[_entryIndex]);
                    _phase = Phase.Screenshot;
                }
            }
        }

        private void NextEntry()
        {
            _entryIndex++;
            if (_entryIndex >= _entries.Length)
            {
                Finish();
                return;
            }
            _phase = Phase.Warmup;
            _phaseSeconds = 0;
            _entrySeconds = 0;
        }

        private void Finish()
        {
            WriteResults();
            Finished = true;
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
            double inactive = _frameMs.Count == 0 ? 0 : (double)_inactiveFrames / _frameMs.Count;
            if (_inactiveFrames > 0)
                _anyInactive = true;
            _resultRows.Add($"| {entry.Scene.Page} | {entry.Scene.Name} | {entry.Scene.Count} | {entry.Kind} | {entry.Renderer} | {fps:0} | {median:0.000} | {p95:0.000} | {draw:0.000} | {inactive:0%} |  |");
        }

        private void RecordSkipped(PerfEntry entry, string reason) =>
            _resultRows.Add($"| {entry.Scene.Page} | {entry.Scene.Name} | {entry.Scene.Count} | {entry.Kind} | {entry.Renderer} | n/a |  |  |  |  | {reason} |");

        private static double Percentile(List<double> sorted, double p) =>
            sorted.Count == 0 ? 0 : sorted[(int)Math.Min(sorted.Count - 1, Math.Round(p * (sorted.Count - 1)))];

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
            sb.AppendLine($"- GPU: {_host.GpuDescription}");
            sb.AppendLine($"- Power: {machine.Power}");
            sb.AppendLine($"- Window: {Width}x{Height}, vsync off, {_options.MeasureSeconds:0}s measured per entry after {_options.WarmupSeconds:0}s warmup");
            sb.AppendLine($"- Skia antialiasing: {(_options.Antialias ? "on" : "off (--no-aa)")}");
            sb.AppendLine($"- MSAA: {_host.MsaaDescription}");
            if (_anyInactive)
                sb.AppendLine("- **Warning: the window was in the background for some measured frames (Inactive column). Those rows may be throttled; rerun with the window focused.**");
            sb.AppendLine();
            sb.AppendLine("| Page | Scene | Count | Kind | Renderer | FPS | Median frame ms | p95 frame ms | Median draw CPU ms | Inactive | Note |");
            sb.AppendLine("|---|---|---:|---|---|---:|---:|---:|---:|---:|---|");
            foreach (var row in _resultRows)
                sb.AppendLine(row);

            string fileName = $"perf-results-{Slug(_label)}.md";
            if (PerfEnvironment.Publish != null)
                PerfEnvironment.Publish(fileName, sb.ToString());
            else
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, fileName), sb.ToString());
        }

        private static string Slug(string text) =>
            new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
    }
}
