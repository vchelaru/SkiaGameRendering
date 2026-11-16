using SkiaGameRendering;
using SkiaSharp;
using Xunit;

namespace Tests.Brutal;

// A cached surface's canvas is the same SKCanvas object every pass; a new surface gives a new one, and a disposed
// surface disposes its canvas (Handle becomes zero). That is what these tests read the cache through.
public sealed class BrutalDirectSurfaceCacheTests
{
    const uint Rgba8 = 37, Bgra8 = 44, Undefined = 0, ColorAttachment = 2, ShaderRead = 5;

    static SKCanvas Pass(SkiaDirectCanvas direct, ulong image, uint format, int width, int height, uint layout, Action<SKCanvas>? draw = null)
    {
        direct.Begin(image, format, width, height, layout);
        var canvas = direct.Canvas;
        draw?.Invoke(canvas);
        direct.End(finalLayout: ShaderRead, synchronous: true);
        return canvas;
    }

    [Fact]
    public void SameImage_ReusesTheSurface_AndEachPassStartsWithNoMatrix()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context, cacheSurfaces: true);
            var first = Pass(direct, image.Image, Rgba8, 16, 16, Undefined, c => { c.Clear(SKColors.Black); c.Translate(8, 8); });
            var second = Pass(direct, image.Image, Rgba8, 16, 16, ShaderRead, c =>
            {
                using var paint = new SKPaint { Color = new SKColor(255, 0, 0) };
                c.DrawRect(0, 0, 4, 4, paint);
            });

            Assert.Same(first, second);
            // The first pass's translate did not carry over: the rect landed at the origin.
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, vk.ReadAppImage(image, 16, 16)[..4]);
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void WithoutCache_EachPassWrapsANewSurface()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context);
            var first = Pass(direct, image.Image, Rgba8, 16, 16, Undefined);
            Assert.Equal(IntPtr.Zero, first.Handle);
            Assert.NotSame(first, Pass(direct, image.Image, Rgba8, 16, 16, ShaderRead));
        }
        finally { vk.Destroy(image); }
    }

    // Swapchain recreation can hand back an image whose handle equals a destroyed one. The driver can't be made to do
    // that on demand, so the same handle stands in for it: after the release, that handle must get a new surface.
    [Fact]
    public void AfterRelease_AnImageWithTheSameHandle_GetsANewSurface()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context, cacheSurfaces: true);
            var stale = Pass(direct, image.Image, Rgba8, 16, 16, Undefined);
            direct.ReleaseCachedSurfaces();
            Assert.Equal(IntPtr.Zero, stale.Handle);

            var fresh = Pass(direct, image.Image, Rgba8, 16, 16, Undefined, c => c.Clear(new SKColor(0, 0, 255)));
            Assert.NotSame(stale, fresh);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, vk.ReadAppImage(image, 16, 16)[..4]);
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void FormatOrSizeChange_MissesTheCache()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context, cacheSurfaces: true);
            var full = Pass(direct, image.Image, Rgba8, 16, 16, Undefined);
            var smaller = Pass(direct, image.Image, Rgba8, 8, 8, ShaderRead);
            var bgra = Pass(direct, image.Image, Bgra8, 16, 16, ShaderRead);

            Assert.NotSame(full, smaller);
            Assert.NotSame(full, bgra);
            Assert.NotSame(smaller, bgra);
            Assert.Same(full, Pass(direct, image.Image, Rgba8, 16, 16, ShaderRead));
        }
        finally { vk.Destroy(image); }
    }

    [Fact]
    public void Dispose_ReleasesCachedSurfaces()
    {
        using var vk = new BrutalDevice();
        var a = vk.CreateAppImage(16, 16, Rgba8);
        var b = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            var direct = new SkiaDirectCanvas(vk.Context, cacheSurfaces: true);
            var first = Pass(direct, a.Image, Rgba8, 16, 16, Undefined);
            var second = Pass(direct, b.Image, Rgba8, 16, 16, Undefined);
            Assert.NotEqual(IntPtr.Zero, first.Handle);
            Assert.NotEqual(IntPtr.Zero, second.Handle);

            direct.Dispose();
            Assert.Equal(IntPtr.Zero, first.Handle);
            Assert.Equal(IntPtr.Zero, second.Handle);
        }
        finally { vk.Destroy(a); vk.Destroy(b); }
    }

    [Fact]
    public void ReleaseBetweenBeginAndEnd_Throws()
    {
        using var vk = new BrutalDevice();
        var image = vk.CreateAppImage(16, 16, Rgba8);
        try
        {
            using var direct = new SkiaDirectCanvas(vk.Context, cacheSurfaces: true);
            direct.Begin(image.Image, Rgba8, 16, 16, Undefined);
            Assert.Throws<InvalidOperationException>(direct.ReleaseCachedSurfaces);
            direct.End(finalLayout: ColorAttachment, synchronous: true);
        }
        finally { vk.Destroy(image); }
    }
}
