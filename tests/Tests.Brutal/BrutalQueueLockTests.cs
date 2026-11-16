using SkiaGameRendering;
using SkiaSharp;
using Xunit;

namespace Tests.Brutal;

/// <summary>
/// The app's queue lock is held while <see cref="SkiaRenderTarget2D.End"/> and <see cref="SkiaDirectCanvas.End"/>
/// submit, never while the app draws on the canvas, so another thread submitting to the queue is not blocked
/// for the whole frame build.
/// </summary>
public sealed class BrutalQueueLockTests
{
    [Fact]
    public void RenderTarget_LockIsNotHeldWhileDrawing_AndIsTakenByEnd()
    {
        var held = 0;
        var acquisitions = 0;
        using var vk = new BrutalDevice(() =>
        {
            held++;
            acquisitions++;
            return new Release(() => held--);
        });
        using var target = new SkiaRenderTarget2D(vk.Context, 16, 16);

        target.Begin();
        Assert.Equal(0, held);
        target.Canvas.Clear(new SKColor(255, 0, 0));
        Assert.Equal(0, held);
        acquisitions = 0;
        target.End(synchronous: true);

        Assert.Equal(0, held);
        // Skia's flush, then the transition back to SHADER_READ_ONLY_OPTIMAL.
        Assert.Equal(2, acquisitions);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, vk.ReadPixel(target, 8, 8));
    }

    [Fact]
    public void DirectCanvas_LockIsNotHeldWhileDrawing_AndIsTakenByEnd()
    {
        var held = 0;
        var acquisitions = 0;
        using var vk = new BrutalDevice(() =>
        {
            held++;
            acquisitions++;
            return new Release(() => held--);
        });
        var image = vk.CreateAppImage(16, 16, VkFormatR8G8B8A8Unorm);
        try
        {
            using (var canvas = new SkiaDirectCanvas(vk.Context))
            {
                canvas.Begin(image.Image, image.Format, 16, 16, currentLayout: 0);
                Assert.Equal(0, held);
                canvas.Canvas.Clear(new SKColor(0, 255, 0));
                Assert.Equal(0, held);
                acquisitions = 0;
                canvas.End(synchronous: true, finalLayout: 5);
                Assert.Equal(0, held);
                Assert.True(acquisitions >= 1, "End did not take the queue lock for Skia's submit.");
            }
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, vk.ReadAppImage(image, 16, 16)[..4]);
        }
        finally
        {
            vk.Destroy(image);
        }
    }

    const uint VkFormatR8G8B8A8Unorm = 37;

    sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
