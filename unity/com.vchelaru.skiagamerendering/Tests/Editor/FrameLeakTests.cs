using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;

namespace SkiaGameRendering.Unity.Tests
{
    /// <summary>
    /// Draws one target hundreds of times and fails if anything grows per frame: the device's
    /// references, on D3D11 the references on the empty context state every draw swaps in and out (a
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
                void Run(int count, bool draw = true)
                {
                    for (int i = 0; i < count; i++)
                    {
                        // On Metal, ReadPixels autoreleases objects that retain the device, and this
                        // loop never returns to Unity's frame loop to drain them, so drain them here.
                        var pool = GpuProbe.PushAutoreleasePool();
                        if (draw)
                            DomainReloadTests.Draw(target);
                        // Reading a pixel back waits for the render thread to run the queued draw.
                        var previous = RenderTexture.active;
                        RenderTexture.active = target.Texture;
                        probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        RenderTexture.active = previous;
                        GpuProbe.PopAutoreleasePool(pool);
                    }
                }

                Run(WarmupFrames);
                // On Metal, Unity's own retains on the device creep up with ReadPixels alone (about 2
                // per 250 frames), so measure that without Skia and only hold Skia to the excess.
                int unityDrift = 0;
                if (GpuProbe.IsMetal)
                {
                    int before = GpuProbe.DeviceRefCount();
                    Run(2 * Frames, draw: false);
                    unityDrift = GpuProbe.DeviceRefCount() - before;
                }
                var emptyState = EmptyStateOfTheFactory();
                var start = Sample(emptyState);
                Run(Frames);
                var middle = Sample(emptyState);
                Run(Frames);
                var end = Sample(emptyState);

                Debug.Log($"{WarmupFrames} warm-up + {2 * Frames} frames: Unity's own device-ref drift {unityDrift}, " +
                    $"device refs {start.Device} -> {middle.Device} -> {end.Device}, " +
                    $"empty-state refs {start.EmptyState} -> {middle.EmptyState} -> {end.EmptyState}, " +
                    $"private MB {start.Memory >> 20} -> {middle.Memory >> 20} -> {end.Memory >> 20}");
                Assert.LessOrEqual(end.Device - start.Device - unityDrift, DomainReloadTests.AllowedGrowth, "Device references grew while drawing frames.");
                Assert.LessOrEqual(end.EmptyState - start.EmptyState, DomainReloadTests.AllowedGrowth, "The empty context state's references grew while drawing frames.");
                Assert.Less(end.Memory - middle.Memory, 32L << 20, "Private memory grew over the last frames.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        static (int Device, int EmptyState, long Memory) Sample(IntPtr emptyState) =>
            (GpuProbe.DeviceRefCount(), emptyState == IntPtr.Zero ? 0 : RefCount(emptyState), GpuProbe.PrivateBytes());

        // The ID3D11DeviceContextState AngleSkiaSurfaceFactory swaps in for every draw, or zero on
        // Metal, which has none. The fields are private, so a rename fails this test with a null
        // reference rather than passing silently.
        static IntPtr EmptyStateOfTheFactory()
        {
            if (GpuProbe.IsMetal)
                return IntPtr.Zero;
            const System.Reflection.BindingFlags nonPublic = System.Reflection.BindingFlags.NonPublic;
            var backend = typeof(SkiaUnityRenderTarget).Assembly
                .GetType("SkiaGameRendering.Unity.SkiaUnityRenderThread", throwOnError: true)
                .GetField("_backend", nonPublic | System.Reflection.BindingFlags.Static).GetValue(null)
                ?? throw new InvalidOperationException("The render thread has not created its backend.");
            var factory = backend.GetType().GetField("_factory", nonPublic | System.Reflection.BindingFlags.Instance).GetValue(backend);
            return (IntPtr)factory.GetType().GetField("_emptyState", nonPublic | System.Reflection.BindingFlags.Instance).GetValue(factory);
        }

        static int RefCount(IntPtr unknown)
        {
            Marshal.AddRef(unknown);
            return Marshal.Release(unknown);
        }
    }
}
