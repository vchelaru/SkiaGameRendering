namespace Performance
{
    /// <summary>
    /// The command line every performance app takes, whichever engine hosts it.
    /// <list type="bullet">
    /// <item><c>--page slug[,slug]</c> runs only those pages' scenes.</item>
    /// <item><c>--scenes "name[,name]"</c> runs only the scenes with those names (exact, ignoring case); combines with <c>--page</c>.</item>
    /// <item><c>--scene N</c> holds entry N (0-based, after the page filter) indefinitely instead of sweeping.</item>
    /// <item><c>--no-screenshots</c> skips the PNGs.</item>
    /// <item><c>--quick</c> shortens the warmup and measured windows for a smoke run; its results are not for the docs.</item>
    /// <item><c>--no-aa</c> turns off antialiasing on every paint and font the Skia drawer uses. Image sampling is unchanged.</item>
    /// <item><c>--msaa N</c> asks for N samples per pixel (2, 4 or 8) on the window and on the Skia render target.</item>
    /// </list>
    /// </summary>
    public sealed class PerfOptions
    {
        public string[]? Pages { get; private init; }
        public string[]? SceneNames { get; private init; }
        public int? HeldEntry { get; private init; }
        public bool Screenshots { get; private init; } = true;
        public bool Quick { get; private init; }
        public bool Antialias { get; private init; } = true;

        /// <summary>Samples per pixel requested with <c>--msaa</c>; 0 when it was not passed.</summary>
        public int MultiSampleCount { get; private init; }

        public double WarmupSeconds => Quick ? 0.3 : 2.0;
        public double MeasureSeconds => Quick ? 0.5 : 4.0;

        public static PerfOptions Parse(string[] args)
        {
            string? Value(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            int msaa = 0;
            if (Value("--msaa") is { } msaaText)
            {
                msaa = int.Parse(msaaText);
                if (msaa is not (0 or 2 or 4 or 8))
                    throw new ArgumentException($"--msaa must be 2, 4 or 8, not {msaa}.");
            }

            return new PerfOptions
            {
                Pages = Value("--page")?.Split(','),
                SceneNames = Value("--scenes")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                HeldEntry = Value("--scene") is { } scene ? int.Parse(scene) : null,
                Screenshots = !args.Contains("--no-screenshots"),
                Quick = args.Contains("--quick"),
                Antialias = !args.Contains("--no-aa"),
                MultiSampleCount = msaa,
            };
        }
    }
}
