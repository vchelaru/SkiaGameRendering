using System.Runtime.InteropServices;
using SkiaGameRendering;
using SkiaSharp;
using Xunit;

namespace Tests.Brutal.OGL;

/// <summary>
/// Skia draws on the app's own GL context, so End must hand back the app's GL state. An antialiased clip makes Skia
/// turn the scissor test on, which a glBlitFramebuffer or glClear after End would otherwise inherit.
/// </summary>
[Collection("BrutalGlfw")]
public sealed unsafe class BrutalOglStateTests
{
    const int Size = 64;
    const int GL_SCISSOR_TEST = 0x0C11, GL_BLEND = 0x0BE2, GL_STENCIL_TEST = 0x0B90;
    const int GL_SCISSOR_BOX = 0x0C10, GL_VIEWPORT = 0x0BA2, GL_CURRENT_PROGRAM = 0x8B8D;
    const int GL_COLOR_WRITEMASK = 0x0C23, GL_BLEND_SRC_RGB = 0x80C9, GL_UNPACK_ALIGNMENT = 0x0CF5;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate byte IsEnabled(int cap);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Cap(int cap);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Rect(int x, int y, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GetIntegerv(int name, int* data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GetBooleanv(int name, byte* data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BlendFunc(int src, int dst);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void PixelStorei(int name, int value);

    static T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(BrutalGlWindow.GetProc(name));

    static int Query(int name)
    {
        int value;
        Load<GetIntegerv>("glGetIntegerv")(name, &value);
        return value;
    }

    static int[] Query4(int name)
    {
        var values = new int[4];
        fixed (int* p = values)
            Load<GetIntegerv>("glGetIntegerv")(name, p);
        return values;
    }

    static bool Enabled(int cap) => Load<IsEnabled>("glIsEnabled")(cap) != 0;

    static string Snapshot()
    {
        var mask = stackalloc byte[4];
        Load<GetBooleanv>("glGetBooleanv")(GL_COLOR_WRITEMASK, mask);
        return $"scissor={Enabled(GL_SCISSOR_TEST)} box={string.Join(',', Query4(GL_SCISSOR_BOX))} " +
               $"viewport={string.Join(',', Query4(GL_VIEWPORT))} blend={Enabled(GL_BLEND)} " +
               $"blendSrc={Query(GL_BLEND_SRC_RGB)} stencil={Enabled(GL_STENCIL_TEST)} program={Query(GL_CURRENT_PROGRAM)} " +
               $"colorMask={mask[0]}{mask[1]}{mask[2]}{mask[3]} unpack={Query(GL_UNPACK_ALIGNMENT)}";
    }

    // Antialiased rect, round rect and path clips, the last one small, so Skia ends the pass with the scissor test on.
    static void DrawClipped(SKCanvas canvas)
    {
        canvas.Clear(SKColors.Black);
        using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = true };
        canvas.Save();
        canvas.ClipRect(new SKRect(4.5f, 4.5f, 30.5f, 30.5f), antialias: true);
        canvas.DrawCircle(16, 16, 20, paint);
        canvas.Restore();
        canvas.Save();
        canvas.ClipRoundRect(new SKRoundRect(new SKRect(36.5f, 4.5f, 60.5f, 28.5f), 6, 6), antialias: true);
        canvas.DrawCircle(48, 16, 20, paint);
        canvas.Restore();
        canvas.Save();
        using (var builder = new SKPathBuilder())
        {
            builder.MoveTo(48, 36); builder.LineTo(60, 48); builder.LineTo(48, 60); builder.LineTo(36, 48); builder.Close();
            using var path = builder.Detach();
            canvas.ClipPath(path, antialias: true);
        }
        canvas.DrawCircle(48, 48, 20, paint);
        canvas.Restore();
    }

    static void AssertClipped(byte[] rgba)
    {
        int At(int x, int y) => rgba[(y * Size + x) * 4];
        Assert.True(At(16, 16) > 200, "inside the rect clip");
        Assert.True(At(48, 48) > 200, "inside the path clip");
        Assert.True(At(33, 16) < 10, "outside the rect and round rect clips");
        Assert.True(At(37, 37) < 10, "outside the path clip");
    }

    // Two app states: the GL defaults, and one where the app has scissor, blend and other state of its own.
    [GlWindowTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderTarget_End_RestoresTheAppsGlState(bool appStateSet)
    {
        using var window = new BrutalGlWindow(Size, Size);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        SetAppState(appStateSet);
        var before = Snapshot();

        using var target = new SkiaRenderTarget2D(context, Size, Size);
        Assert.Equal(before, Snapshot());
        target.Begin();
        DrawClipped(target.Canvas);
        target.End();

        Assert.Equal(before, Snapshot());
        Assert.Equal(appStateSet, Enabled(GL_SCISSOR_TEST));
        AssertClipped(GlReadback.ReadTexture(BrutalGlWindow.GetProc, target.GlTexture, Size, Size));
    }

    [GlWindowTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectCanvas_End_RestoresTheAppsGlState(bool appStateSet)
    {
        using var window = new BrutalGlWindow(Size, Size);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var direct = new SkiaDirectCanvas(context);
        SetAppState(appStateSet);
        var before = Snapshot();

        direct.Begin();
        DrawClipped(direct.Canvas);
        direct.End();

        Assert.Equal(before, Snapshot());
        Assert.Equal(appStateSet, Enabled(GL_SCISSOR_TEST));
        // Top row first, like the texture.
        AssertClipped(GlReadback.ReadDefaultFramebuffer(BrutalGlWindow.GetProc, Size, Size));
    }

    static void SetAppState(bool set)
    {
        Load<Rect>("glViewport")(0, 0, Size, Size);
        if (!set)
            return;
        Load<Cap>("glEnable")(GL_SCISSOR_TEST);
        Load<Rect>("glScissor")(1, 2, 40, 50);
        Load<Cap>("glEnable")(GL_BLEND);
        Load<BlendFunc>("glBlendFunc")(0x0302 /* SRC_ALPHA */, 0x0303 /* ONE_MINUS_SRC_ALPHA */);
        Load<PixelStorei>("glPixelStorei")(GL_UNPACK_ALIGNMENT, 1);
    }
}

public sealed class GlWindowTheoryAttribute : TheoryAttribute
{
    public GlWindowTheoryAttribute()
    {
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Skip = "GitHub's Windows runner has no OpenGL driver; brutal-ogl-linux runs these tests.";
    }
}
