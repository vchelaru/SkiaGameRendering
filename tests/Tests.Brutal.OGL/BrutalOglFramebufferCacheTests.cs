using System.Runtime.InteropServices;
using SkiaGameRendering;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Brutal.OGL;

/// <summary>
/// <see cref="SkiaDirectCanvas"/> keeps one surface per app FBO across passes, rebuilds it when the attachment or size
/// changes, still draws into a new texture that got a deleted one's GL name, and holds a bounded number of surfaces.
/// </summary>
[Collection("BrutalGlfw")]
public sealed class BrutalOglFramebufferCacheTests
{
    const int GL_FRAMEBUFFER = 0x8D40;
    const int GL_TEXTURE_2D = 0x0DE1;
    const int Size = 64;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenFramebuffers(int n, out int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindFramebuffer(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenTextures(int n, out int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void DeleteTextures(int n, ref int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindTexture(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Viewport(int x, int y, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TexImage2D(int target, int level, int internalFormat, int width, int height, int border, int format, int type, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void FramebufferTexture2D(int target, int attachment, int texTarget, int texture, int level);

    readonly ITestOutputHelper _output;

    public BrutalOglFramebufferCacheTests(ITestOutputHelper output) => _output = output;

    static T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(BrutalGlWindow.GetProc(name));

    static int NewTexture(int size)
    {
        Load<GenTextures>("glGenTextures")(1, out var texture);
        Load<BindTexture>("glBindTexture")(GL_TEXTURE_2D, texture);
        Load<TexImage2D>("glTexImage2D")(GL_TEXTURE_2D, 0, 0x8058 /* GL_RGBA8 */, size, size, 0, 0x1908 /* GL_RGBA */, 0x1401 /* GL_UNSIGNED_BYTE */, IntPtr.Zero);
        return texture;
    }

    static int NewFramebuffer(int texture)
    {
        Load<GenFramebuffers>("glGenFramebuffers")(1, out var framebuffer);
        Attach(framebuffer, texture);
        return framebuffer;
    }

    static void Attach(int framebuffer, int texture)
    {
        Load<BindFramebuffer>("glBindFramebuffer")(GL_FRAMEBUFFER, framebuffer);
        Load<FramebufferTexture2D>("glFramebufferTexture2D")(GL_FRAMEBUFFER, 0x8CE0 /* GL_COLOR_ATTACHMENT0 */, GL_TEXTURE_2D, texture, 0);
    }

    static void Pass(SkiaDirectCanvas direct, int size, SKColor color)
    {
        Load<Viewport>("glViewport")(0, 0, size, size);
        direct.Begin();
        direct.Canvas.Clear(color);
        direct.End();
    }

    static (byte r, byte g, byte b) Pixel(int texture, int size)
    {
        var rgba = GlReadback.ReadTexture(BrutalGlWindow.GetProc, texture, size, size);
        return (rgba[0], rgba[1], rgba[2]);
    }

    [GlWindowFact]
    public void SecondPassIntoTheSameFbo_ReusesTheSurface_AndChangedAttachmentOrSizeRebuildsIt()
    {
        using var window = new BrutalGlWindow(Size, Size);
        var texture = NewTexture(Size);
        var framebuffer = NewFramebuffer(texture);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var direct = new SkiaDirectCanvas(context);
        var cache = direct.FramebufferSurfaces;

        Pass(direct, Size, SKColors.Red);
        Pass(direct, Size, SKColors.Lime);
        Assert.Equal(1, cache.SurfacesCreated);
        Assert.Equal((0, 255, 0), Pixel(texture, Size));

        // Another texture on the same FBO.
        var other = NewTexture(Size);
        Attach(framebuffer, other);
        Pass(direct, Size, SKColors.Blue);
        Assert.Equal(2, cache.SurfacesCreated);
        Assert.Equal((0, 0, 255), Pixel(other, Size));

        // The same texture at a new size.
        Load<BindTexture>("glBindTexture")(GL_TEXTURE_2D, other);
        Load<TexImage2D>("glTexImage2D")(GL_TEXTURE_2D, 0, 0x8058, Size / 2, Size / 2, 0, 0x1908, 0x1401, IntPtr.Zero);
        Load<BindFramebuffer>("glBindFramebuffer")(GL_FRAMEBUFFER, framebuffer);
        Pass(direct, Size / 2, SKColors.Red);
        Assert.Equal(3, cache.SurfacesCreated);
        Assert.Equal((255, 0, 0), Pixel(other, Size / 2));
    }

    [GlWindowFact]
    public void NewTextureUnderADeletedOnesName_IsDrawnInto()
    {
        using var window = new BrutalGlWindow(Size, Size);
        var texture = NewTexture(Size);
        var framebuffer = NewFramebuffer(texture);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var direct = new SkiaDirectCanvas(context);

        Pass(direct, Size, SKColors.Red);

        var deleted = texture;
        Load<DeleteTextures>("glDeleteTextures")(1, ref deleted);
        var replacement = NewTexture(Size);
        Attach(framebuffer, replacement);
        _output.WriteLine($"deleted texture {texture}, new texture {replacement}");

        Pass(direct, Size, SKColors.Lime);
        Assert.Equal((0, 255, 0), Pixel(replacement, Size));
        if (replacement == texture)
            Assert.Equal(1, direct.FramebufferSurfaces.SurfacesCreated);
    }

    [GlWindowFact]
    public void CyclingMoreFbosThanTheCacheHolds_StaysBounded()
    {
        using var window = new BrutalGlWindow(Size, Size);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var direct = new SkiaDirectCanvas(context);

        for (var i = 0; i < GlAttachmentSurfaceCache.MaxEntries * 2 + 1; i++)
        {
            NewFramebuffer(NewTexture(Size));
            Pass(direct, Size, SKColors.Red);
            Assert.InRange(direct.FramebufferSurfaces.Count, 1, GlAttachmentSurfaceCache.MaxEntries);
        }
    }

    // A game draws into its FBO every frame, so a cached pass must not allocate managed memory.
    [GlWindowFact]
    public void BeginEndIntoAnFbo_AllocatesNothing()
    {
        using var window = new BrutalGlWindow(Size, Size);
        NewFramebuffer(NewTexture(Size));
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var direct = new SkiaDirectCanvas(context);
        var viewport = Load<Viewport>("glViewport");
        void Run()
        {
            viewport(0, 0, Size, Size);
            direct.Begin();
            direct.End();
        }
        for (var i = 0; i < 50; i++)
            Run();

        const int Passes = 300;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var i = 0; i < Passes; i++)
            Run();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        _output.WriteLine($"{elapsed.TotalMilliseconds * 1000 / Passes:F1} us and {bytes / (double)Passes:F1} bytes per pass");
        Assert.Equal(0, bytes);
    }
}
