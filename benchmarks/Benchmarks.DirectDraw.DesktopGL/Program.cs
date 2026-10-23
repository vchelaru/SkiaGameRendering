using Benchmarks.DirectDraw;

// Usage: Benchmarks.DirectDraw.DesktopGL --mode offscreen|direct|ab --width 1280 --height 720
//        [--warmup 300] [--frames 3000] [--run 1] [--out results.md] [--capture last-frame.rgba]
var options = BenchmarkOptions.Parse(args);
using var game = new DirectDrawBenchmarkGame(options);
game.RunHidden();

namespace Benchmarks.DirectDraw
{
    sealed record BenchmarkOptions(string Mode, int Width, int Height, int Warmup, int Frames, int Run, string Output,
        string? Capture)
    {
        internal static BenchmarkOptions Parse(string[] args)
        {
            string Get(string name, string fallback)
            {
                var index = Array.IndexOf(args, "--" + name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
            }

            var mode = Get("mode", "direct");
            if (mode is not ("offscreen" or "direct" or "ab"))
                throw new ArgumentException("--mode must be offscreen, direct or ab.");

            return new BenchmarkOptions(mode, int.Parse(Get("width", "1280")), int.Parse(Get("height", "720")),
                int.Parse(Get("warmup", "300")), int.Parse(Get("frames", "3000")), int.Parse(Get("run", "1")),
                Get("out", "results.md"), Get("capture", "") is { Length: > 0 } capture ? capture : null);
        }
    }
}
