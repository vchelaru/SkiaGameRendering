using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Shared;

/// <summary>
/// Runs one frame's worth of Skia drawing many times and fails if a resource grows with the frame
/// count. A leak shows up as growth over hundreds of frames, not in any single frame, so this warms
/// up first (Skia fills its shader and resource caches on the first frames) and then compares the
/// start and end of two equal measured windows.
/// <para>
/// Process-wide numbers are noisy, so their limits are loose: they catch a leak of a texture or a
/// command list per frame, not of a few bytes. The counters passed to
/// <see cref="AssertNoPerFrameGrowth"/> are where the sharp checks go, such as a COM reference count,
/// which a leak moves by one every frame.
/// </para>
/// </summary>
static class FrameLeakCheck
{
    /// <summary>
    /// Every leak test class joins this collection, which runs on its own after the parallel tests,
    /// so another test's device (WARP alone starts a dozen threads) cannot move the process-wide numbers.
    /// </summary>
    internal const string Collection = "Frame leak checks";

    /// <summary>
    /// Stops the run long before a runaway leak can take the machine down with it.
    /// </summary>
    const long PrivateBytesCeiling = 3L * 1024 * 1024 * 1024;

    /// <param name="frame">Draws one frame. Called <c>warmupFrames + 2 * frames</c> times.</param>
    /// <param name="counters">Object counts (name, read) that must hold steady across the second measured window.</param>
    /// <param name="privateBytesLimit">Allowed process private-memory growth across the measured frames.</param>
    internal static void AssertNoPerFrameGrowth(
        Action frame, ITestOutputHelper output,
        int warmupFrames = 50, int frames = 250,
        (string Name, Func<long> Read)[]? counters = null,
        long privateBytesLimit = 24L * 1024 * 1024)
    {
        counters ??= [];
        // A longer local run for measuring, e.g. SKIAGAMERENDERING_LEAK_FRAMES=5000; limits stay fixed.
        if (int.TryParse(Environment.GetEnvironmentVariable("SKIAGAMERENDERING_LEAK_FRAMES"), out var overrideFrames) && overrideFrames > 0)
            frames = overrideFrames;

        Run(frame, warmupFrames);
        var start = Sample(counters);
        Run(frame, frames);
        var middle = Sample(counters);
        Run(frame, frames);
        var end = Sample(counters);

        output.WriteLine($"{warmupFrames} warm-up + {2 * frames} measured frames:");
        output.WriteLine($"  private bytes  {Mb(start.PrivateBytes)} -> {Mb(middle.PrivateBytes)} -> {Mb(end.PrivateBytes)}");
        output.WriteLine($"  managed bytes  {Mb(start.ManagedBytes)} -> {Mb(middle.ManagedBytes)} -> {Mb(end.ManagedBytes)}");
        output.WriteLine($"  handles        {start.Handles} -> {middle.Handles} -> {end.Handles}");
        for (int i = 0; i < counters.Length; i++)
            output.WriteLine($"  {counters[i].Name,-14} {start.Counters[i]} -> {middle.Counters[i]} -> {end.Counters[i]}");

        // Growth in both halves is what a leak looks like; one-off growth in the first half (a cache
        // still filling) is not. ANGLE's device, for one, gains a few children over the first
        // thousands of frames and then holds steady, so a counter gets slack well under one per
        // frame, and only the second half is held to it.
        var counterSlack = Math.Max(4, frames / 20);
        for (int i = 0; i < counters.Length; i++)
            Assert.True(end.Counters[i] - middle.Counters[i] <= counterSlack,
                $"{counters[i].Name} went from {middle.Counters[i]} to {end.Counters[i]} over {frames} frames.");

        Assert.True(end.PrivateBytes - start.PrivateBytes < privateBytesLimit,
            $"Private memory grew {Mb(end.PrivateBytes - start.PrivateBytes)} over {2 * frames} frames.");
        Assert.True(end.PrivateBytes - middle.PrivateBytes < privateBytesLimit / 2,
            $"Private memory grew {Mb(end.PrivateBytes - middle.PrivateBytes)} over the last {frames} frames.");
        Assert.True(end.ManagedBytes - start.ManagedBytes < 2L * 1024 * 1024,
            $"The managed heap grew {Mb(end.ManagedBytes - start.ManagedBytes)} over {2 * frames} frames.");
        Assert.True(end.Handles - start.Handles < 32,
            $"The process handle count grew from {start.Handles} to {end.Handles} over {2 * frames} frames.");
    }

    static void Run(Action frame, int count)
    {
        using var process = Process.GetCurrentProcess();
        for (int i = 0; i < count; i++)
        {
            frame();
            if (i % 50 == 49)
            {
                process.Refresh();
                if (PrivateBytes(process) > PrivateBytesCeiling)
                    throw new InvalidOperationException(
                        $"Private memory reached {Mb(PrivateBytes(process))}; stopping the leak check before it exhausts the machine.");
            }
        }
    }

    /// <summary>
    /// Private bytes, or the working set on a platform where .NET reports private bytes as 0.
    /// </summary>
    static long PrivateBytes(Process process) =>
        process.PrivateMemorySize64 is > 0 and var bytes ? bytes : process.WorkingSet64;

    record struct Snapshot(long PrivateBytes, long ManagedBytes, int Handles, long[] Counters);

    static Snapshot Sample((string Name, Func<long> Read)[] counters)
    {
        // Undisposed Skia wrappers free their native memory from finalizers, so settle those first.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        return new Snapshot(
            PrivateBytes(process),
            GC.GetTotalMemory(forceFullCollection: true),
            process.HandleCount,
            counters.Select(counter => counter.Read()).ToArray());
    }

    static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):F1} MB";
}

[CollectionDefinition(FrameLeakCheck.Collection, DisableParallelization = true)]
public sealed class FrameLeakCollection;
