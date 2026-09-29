using System.Text;

namespace Benchmarks.ShapeRendering
{
    public enum BenchmarkPhase
    {
        Idle,
        Warmup,
        Measuring,
        Finished,
    }

    public sealed class AutoBenchmarkResult
    {
        public string SceneName = "";
        public string RendererName = "";
        public bool UsesSkia;
        public int ShapeCount;
        public double AvgFps;
        public double AvgFrameMs;
        public double AvgRenderMs;
        public double AvgBlitMs;
    }

    /// <summary>
    /// Drives a scripted sweep over every (scene, renderer) pair: a short warmup to let frame
    /// pacing settle, then a measured window whose average frame time/FPS is recorded. Feed it
    /// this frame's timings via <see cref="Tick"/>; when <see cref="Phase"/> reaches
    /// <see cref="BenchmarkPhase.Finished"/>, <see cref="Results"/> holds one row per pair.
    ///
    /// This exists because real numbers require an actual GPU window on the user's machine - this
    /// class is what lets the app measure and report itself instead of the user reading a live HUD.
    /// </summary>
    public sealed class AutoBenchmark
    {
        private const double WarmupSeconds = 1.0;
        private const double MeasureSeconds = 3.0;

        private readonly Queue<(int SceneIndex, RendererKind Renderer)> _queue = new();
        private readonly List<double> _frameMs = new();
        private readonly List<double> _renderMs = new();
        private readonly List<double> _blitMs = new();
        private double _phaseElapsed;

        public BenchmarkPhase Phase { get; private set; } = BenchmarkPhase.Idle;
        public int SceneIndex { get; private set; }
        public RendererKind Renderer { get; private set; }
        public List<AutoBenchmarkResult> Results { get; } = new();
        public string StatusLine { get; private set; } = "";

        public void Start(Scene[] scenes)
        {
            _queue.Clear();
            Results.Clear();
            for (int s = 0; s < scenes.Length; s++)
            {
                foreach (var renderer in scenes[s].Renderers)
                    _queue.Enqueue((s, renderer));
            }
            AdvanceToNext();
        }

        private void AdvanceToNext()
        {
            if (_queue.Count == 0)
            {
                Phase = BenchmarkPhase.Finished;
                StatusLine = "Benchmark complete - results written to benchmark-results.md";
                return;
            }

            (SceneIndex, Renderer) = _queue.Dequeue();
            Phase = BenchmarkPhase.Warmup;
            _phaseElapsed = 0;
            _frameMs.Clear();
            _renderMs.Clear();
            _blitMs.Clear();
        }

        /// <summary>Call once per frame while <see cref="Phase"/> is not Idle/Finished.</summary>
        public void Tick(double frameMs, double renderMs, double blitMs, string sceneName, int shapeCount)
        {
            if (Phase is BenchmarkPhase.Idle or BenchmarkPhase.Finished)
                return;

            _phaseElapsed += frameMs / 1000.0;

            if (Phase == BenchmarkPhase.Warmup)
            {
                StatusLine = $"Benchmarking {sceneName} / {Renderer.Label()} - warming up ({_phaseElapsed:0.0}s / {WarmupSeconds:0.0}s)";
                if (_phaseElapsed >= WarmupSeconds)
                {
                    Phase = BenchmarkPhase.Measuring;
                    _phaseElapsed = 0;
                }
                return;
            }

            _frameMs.Add(frameMs);
            _renderMs.Add(renderMs);
            _blitMs.Add(blitMs);
            StatusLine = $"Benchmarking {sceneName} / {Renderer.Label()} - measuring ({_phaseElapsed:0.0}s / {MeasureSeconds:0.0}s)";

            if (_phaseElapsed >= MeasureSeconds)
            {
                double avgFrame = Average(_frameMs);
                Results.Add(new AutoBenchmarkResult
                {
                    SceneName = sceneName,
                    RendererName = Renderer.Label(),
                    UsesSkia = Renderer.UsesSkia(),
                    ShapeCount = shapeCount,
                    AvgFrameMs = avgFrame,
                    AvgFps = avgFrame > 0 ? 1000.0 / avgFrame : 0,
                    AvgRenderMs = Average(_renderMs),
                    AvgBlitMs = Average(_blitMs),
                });
                AdvanceToNext();
            }
        }

        private static double Average(List<double> values)
        {
            if (values.Count == 0)
                return 0;

            double sum = 0;
            foreach (var v in values)
                sum += v;
            return sum / values.Count;
        }

        public string ToMarkdownTable()
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Scene | Count | Renderer | Avg FPS | Frame ms (real) | Draw ms (CPU submit) | Blit ms (CPU submit) |");
            sb.AppendLine("|---|---:|---|---:|---:|---:|---:|");
            foreach (var r in Results)
            {
                string blit = r.UsesSkia ? r.AvgBlitMs.ToString("0.000") : "-";
                sb.AppendLine($"| {r.SceneName} | {r.ShapeCount} | {r.RendererName} | {r.AvgFps:0.0} | {r.AvgFrameMs:0.000} | {r.AvgRenderMs:0.000} | {blit} |");
            }
            return sb.ToString();
        }
    }
}
