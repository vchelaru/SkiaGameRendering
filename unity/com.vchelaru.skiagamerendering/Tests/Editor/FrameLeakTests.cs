using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace SkiaGameRendering.Unity.Tests
{
    /// <summary>
    /// Draws one target hundreds of times and fails if anything grows per frame: the D3D11 device's
    /// references, the references on the empty D3D11 context state every draw swaps in and out (a
    /// missed Release there climbs by one a frame), or the process's private memory. Warms up first,
    /// and holds only the second half of the run to a loose memory limit, since the Editor's own
    /// allocations move that number too. Runs with the other Editor tests (see the repo's CLAUDE.md).
    /// </summary>
    public sealed class FrameLeakTests
    {
        const int WarmupFrames = 50;
        const int Frames = 250;

        [Test]
        public void DrawingFramesGrowsNothing()
        {
            using var target = new SkiaUnityRenderTarget(DomainReloadTests.Size, DomainReloadTests.Size);
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                void Run(int count)
                {
                    for (int i = 0; i < count; i++)
                    {
                        DomainReloadTests.Draw(target);
                        // Reading a pixel back waits for the render thread to run the queued draw.
                        var previous = RenderTexture.active;
                        RenderTexture.active = target.Texture;
                        probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        RenderTexture.active = previous;
                    }
                }

                Run(WarmupFrames);
                var emptyState = EmptyStateOfTheFactory();
                var start = Sample(emptyState);
                Run(Frames);
                var middle = Sample(emptyState);
                Run(Frames);
                var end = Sample(emptyState);

                Debug.Log($"{WarmupFrames} warm-up + {2 * Frames} frames: " +
                    $"device refs {start.Device} -> {middle.Device} -> {end.Device}, " +
                    $"empty-state refs {start.EmptyState} -> {middle.EmptyState} -> {end.EmptyState}, " +
                    $"private MB {start.Memory >> 20} -> {middle.Memory >> 20} -> {end.Memory >> 20}");
                Assert.LessOrEqual(end.Device - start.Device, DomainReloadTests.AllowedGrowth, "D3D11 device references grew while drawing frames.");
                Assert.LessOrEqual(end.EmptyState - start.EmptyState, DomainReloadTests.AllowedGrowth, "The empty context state's references grew while drawing frames.");
                Assert.Less(end.Memory - middle.Memory, 32L << 20, "Private memory grew over the last frames.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        static (int Device, int EmptyState, long Memory) Sample(IntPtr emptyState) =>
            (DomainReloadTests.DeviceRefCount(), RefCount(emptyState), PrivateBytes());

        // Unity's Mono reports Process.PrivateMemorySize64 as 0, so ask Windows directly.
        static long PrivateBytes()
        {
            var counters = new ProcessMemoryCountersEx { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>() };
            if (!K32GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size))
                throw new InvalidOperationException($"GetProcessMemoryInfo failed: {Marshal.GetLastWin32Error()}");
            return (long)counters.PrivateUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessMemoryCountersEx
        {
            public uint Size;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool K32GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx counters, uint size);

        // The ID3D11DeviceContextState AngleSkiaSurfaceFactory swaps in for every draw. Both fields are
        // private, so a rename fails this test with a null reference rather than passing silently.
        static IntPtr EmptyStateOfTheFactory()
        {
            const System.Reflection.BindingFlags nonPublic = System.Reflection.BindingFlags.NonPublic;
            var factory = typeof(SkiaUnityRenderTarget).Assembly
                .GetType("SkiaGameRendering.Unity.SkiaUnityRenderThread", throwOnError: true)
                .GetField("_factory", nonPublic | System.Reflection.BindingFlags.Static).GetValue(null)
                ?? throw new InvalidOperationException("The render thread has not created its ANGLE factory.");
            return (IntPtr)factory.GetType().GetField("_emptyState", nonPublic | System.Reflection.BindingFlags.Instance).GetValue(factory);
        }

        static int RefCount(IntPtr unknown)
        {
            Marshal.AddRef(unknown);
            return Marshal.Release(unknown);
        }
    }
}
