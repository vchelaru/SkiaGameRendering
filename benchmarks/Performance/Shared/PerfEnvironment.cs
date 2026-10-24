namespace Performance
{
    /// <summary>
    /// Hooks for a platform that has no file system or cannot see its own hardware (the browser). Desktop
    /// apps leave everything unset.
    /// </summary>
    public static class PerfEnvironment
    {
        /// <summary>Receives the results file name and its markdown instead of writing the file.</summary>
        public static Action<string, string>? Publish { get; set; }

        /// <summary>Overrides the OS, CPU, power and commit lines of the results header.</summary>
        public static Func<MachineDescription>? Machine { get; set; }

        /// <summary>The GPU name, when the platform's graphics API cannot report it.</summary>
        public static string? Gpu { get; set; }

        /// <summary>False where the back buffer cannot be saved as a PNG.</summary>
        public static bool Screenshots { get; set; } = true;
    }

    public sealed record MachineDescription(string Os, string Cpu, string Power, string Commit);
}
