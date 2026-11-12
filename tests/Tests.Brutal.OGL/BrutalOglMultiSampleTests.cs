using System.Runtime.InteropServices;
using SkiaGameRendering;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Brutal.OGL;

/// <summary><see cref="SkiaRenderTarget2D"/>'s <c>multiSampleCount</c>: Skia draws 4x and resolves into the single-sample texture.</summary>
[Collection("BrutalGlfw")]
public sealed class BrutalOglMultiSampleTests
{
    const int GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6;
    const int GL_FRAMEBUFFER = 0x8D40;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] unsafe delegate void GetIntegerv(int name, int* data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenFramebuffers(int n, out int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindFramebuffer(int target, int id);

    readonly ITestOutputHelper _output;

    public BrutalOglMultiSampleTests(ITestOutputHelper output) => _output = output;

    [GlWindowFact]
    public unsafe void FourSamples_ResolveAntialiasedEdgesIntoTheTexture_AndRestoreTheAppsFramebuffer()
    {
        const int Size = 64;
        using var window = new BrutalGlWindow(Size, Size);
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(BrutalGlWindow.GetProc(name));
        Load<GenFramebuffers>("glGenFramebuffers")(1, out var appFramebuffer);
        Load<BindFramebuffer>("glBindFramebuffer")(GL_FRAMEBUFFER, appFramebuffer);

        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var target = new SkiaRenderTarget2D(context, Size, Size, multiSampleCount: 4);

        // Two passes, the second drawing over the first, so the resolve runs again on kept contents.
        target.Begin();
        MultiSampleChecks.DrawTriangle(target.Canvas);
        target.End();
        target.Begin(clear: false);
        using (var paint = new SKPaint { Color = SKColors.Blue })
            target.Canvas.DrawRect(0, 0, 4, 4, paint);
        target.End();

        int bound;
        Load<GetIntegerv>("glGetIntegerv")(GL_DRAW_FRAMEBUFFER_BINDING, &bound);
        Assert.Equal(appFramebuffer, bound);

        var rgba = GlReadback.ReadTexture(BrutalGlWindow.GetProc, target.GlTexture, Size, Size);
        MultiSampleChecks.AssertFourSampleTriangle(rgba, Size, _output);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, rgba[..4]);
    }

    [GlWindowFact]
    public void InvalidCountsThrow()
    {
        using var window = new BrutalGlWindow(16, 16);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        foreach (var count in new[] { -1, 1, 3, 16 })
            Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(context, 16, 16, multiSampleCount: count));
    }
}
