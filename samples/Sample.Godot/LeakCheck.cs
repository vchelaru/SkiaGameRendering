using System.Diagnostics;
using Godot;

/// <summary>
/// The sample's <c>--leak-check N</c> mode, which tests/Tests.Godot drives: after a warm-up, samples
/// the process and Godot's own counters at the start, middle and end of N frames, prints them on
/// one line starting "Leak check:", and asks the sample to quit. Add <c>--no-skia</c> to run the
/// same frames with the Skia draw skipped, for comparing growth with Skia on and off.
/// </summary>
sealed class LeakCheck(int frames)
{
    const int WarmupFrames = 60;

    readonly long[] _privateBytes = new long[3];
    readonly long[] _handles = new long[3];
    readonly long[] _objects = new long[3];
    readonly long[] _videoMemory = new long[3];
    int _frame;

    /// <summary>Call once per rendered frame. Returns true once the run is over and the line is printed.</summary>
    public bool Frame()
    {
        _frame++;
        int index = _frame == WarmupFrames ? 0
            : _frame == WarmupFrames + frames / 2 ? 1
            : _frame == WarmupFrames + frames ? 2
            : -1;
        if (index < 0)
            return false;

        using var process = Process.GetCurrentProcess();
        _privateBytes[index] = process.PrivateMemorySize64 is > 0 and var bytes ? bytes : process.WorkingSet64;
        _handles[index] = process.HandleCount;
        _objects[index] = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
        _videoMemory[index] = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
        if (index < 2)
            return false;

        GD.Print($"Leak check: frames={frames} privateBytes={Join(_privateBytes)} handles={Join(_handles)} " +
            $"objects={Join(_objects)} videoMemory={Join(_videoMemory)}");
        return true;
    }

    static string Join(long[] values) => string.Join("/", values);
}
