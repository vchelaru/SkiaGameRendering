using System.Runtime.InteropServices;
using SkiaGameRendering;
using SkiaSharp;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Tests.Brutal.OGL;

/// <summary>
/// Draws the shared <see cref="GoldenScene"/> through <see cref="SkiaRenderTarget2D"/> on a BRUTAL GLFW OpenGL 3.3 core
/// context, reads the texture back with glReadPixels (test-only) and compares it to the golden. The golden was rendered by
/// CI's Mesa llvmpipe, so the pixel comparison only runs under SKIAGAMERENDERING_PINNED_RASTERIZER; orientation is always
/// checked. Needs a display (CI uses Xvfb).
/// </summary>
[Collection("BrutalGlfw")]
public sealed class BrutalOglTests
{
    const int GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6;
    const int GL_FRAMEBUFFER = 0x8D40;
    const int GL_TEXTURE_BINDING_2D = 0x8069;
    const int GL_TEXTURE_2D = 0x0DE1;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] unsafe delegate void GetIntegerv(int name, int* data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenFramebuffers(int n, out int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindFramebuffer(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenTextures(int n, out int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindTexture(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Viewport(int x, int y, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TexImage2D(int target, int level, int internalFormat, int width, int height, int border, int format, int type, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void FramebufferTexture2D(int target, int attachment, int texTarget, int texture, int level);

    readonly ITestOutputHelper _output;

    public BrutalOglTests(ITestOutputHelper output) => _output = output;

    [GlWindowFact]
    public void Scene_MatchesGolden_ThroughBrutalGlfwContext()
    {
        using var window = new BrutalGlWindow(GoldenScene.Width, GoldenScene.Height);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var target = new SkiaRenderTarget2D(context, GoldenScene.Width, GoldenScene.Height);
        target.Begin();
        GoldenScene.Draw(target.Canvas);
        target.End();

        var pixels = GlReadback.ReadTexture(BrutalGlWindow.GetProc, target.GlTexture, GoldenScene.Width, GoldenScene.Height);
        SaveScreenshot(pixels);

        GoldenImage.AssertSceneOrientation(pixels);
        if (GoldenImage.PinnedRasterizerInUse)
            GoldenImage.AssertMatchesGolden(pixels, "brutal-ogl-scene.png");
        else
            _output.WriteLine("Golden comparison skipped: the golden was rendered by CI's pinned Mesa llvmpipe and this run " +
                "used whichever GL driver this machine has. Orientation was still checked.");
    }

    [GlWindowFact]
    public unsafe void Begin_End_And_Create_LeaveTheAppsFramebufferAndTextureBound()
    {
        using var window = new BrutalGlWindow(64, 64);
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(BrutalGlWindow.GetProc(name));
        int Query(int name) { int v; Load<GetIntegerv>("glGetIntegerv")(name, &v); return v; }

        // The app's own framebuffer and texture, bound before Skia does anything.
        Load<GenFramebuffers>("glGenFramebuffers")(1, out var appFramebuffer);
        Load<BindFramebuffer>("glBindFramebuffer")(GL_FRAMEBUFFER, appFramebuffer);
        Load<GenTextures>("glGenTextures")(1, out var appTexture);
        Load<BindTexture>("glBindTexture")(GL_TEXTURE_2D, appTexture);

        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var target = new SkiaRenderTarget2D(context, 64, 64);
        Assert.Equal(appFramebuffer, Query(GL_DRAW_FRAMEBUFFER_BINDING));
        Assert.Equal(appTexture, Query(GL_TEXTURE_BINDING_2D));

        target.Begin();
        Assert.NotEqual(appFramebuffer, Query(GL_DRAW_FRAMEBUFFER_BINDING));
        target.Canvas.Clear(SKColors.Red);
        target.End();
        Assert.Equal(appFramebuffer, Query(GL_DRAW_FRAMEBUFFER_BINDING));
        Assert.Equal(appTexture, Query(GL_TEXTURE_BINDING_2D));
    }

    [GlWindowFact]
    public void Scene_DrawnWithDirectCanvasIntoBackBuffer_MatchesGolden()
    {
        using var window = new BrutalGlWindow(GoldenScene.Width, GoldenScene.Height);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var direct = new SkiaDirectCanvas(context);
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(BrutalGlWindow.GetProc(name));
        // Twice: the second pass reuses the cached back buffer surface.
        byte[] pixels = [];
        for (var i = 0; i < 2; i++)
        {
            // Begin takes the size from the viewport, which is undefined after a pass.
            Load<Viewport>("glViewport")(0, 0, GoldenScene.Width, GoldenScene.Height);
            direct.Begin();
            GoldenScene.Draw(direct.Canvas);
            direct.End();
            pixels = GlReadback.ReadDefaultFramebuffer(BrutalGlWindow.GetProc, GoldenScene.Width, GoldenScene.Height);
        }

        GoldenImage.AssertSceneOrientation(pixels);
        if (GoldenImage.PinnedRasterizerInUse)
            GoldenImage.AssertMatchesGolden(pixels, "brutal-ogl-scene.png");
        else
            _output.WriteLine("Golden comparison skipped: this run used whichever GL driver this machine has. Orientation was still checked.");
    }

    [GlWindowFact]
    public unsafe void DirectCanvas_IntoAppFbo_DrawsTopDownAndRestoresTheFramebuffer()
    {
        const int Size = 64;
        using var window = new BrutalGlWindow(Size, Size);
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(BrutalGlWindow.GetProc(name));
        int Query(int name) { int v; Load<GetIntegerv>("glGetIntegerv")(name, &v); return v; }

        Load<GenTextures>("glGenTextures")(1, out var texture);
        Load<BindTexture>("glBindTexture")(GL_TEXTURE_2D, texture);
        Load<TexImage2D>("glTexImage2D")(GL_TEXTURE_2D, 0, 0x8058 /* GL_RGBA8 */, Size, Size, 0, 0x1908 /* GL_RGBA */, 0x1401 /* GL_UNSIGNED_BYTE */, IntPtr.Zero);
        Load<GenFramebuffers>("glGenFramebuffers")(1, out var appFramebuffer);
        Load<BindFramebuffer>("glBindFramebuffer")(GL_FRAMEBUFFER, appFramebuffer);
        Load<FramebufferTexture2D>("glFramebufferTexture2D")(GL_FRAMEBUFFER, 0x8CE0 /* GL_COLOR_ATTACHMENT0 */, GL_TEXTURE_2D, texture, 0);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var direct = new SkiaDirectCanvas(context);
        // Twice, so the second pass checks the per-pass FBO is rebuilt and freed cleanly.
        for (var i = 0; i < 2; i++)
        {
            Load<Viewport>("glViewport")(0, 0, Size, Size);
            direct.Begin();
            direct.Canvas.Clear(SKColors.Red);
            using (var paint = new SKPaint { Color = SKColors.Lime })
                direct.Canvas.DrawRect(0, 0, 16, 16, paint);
            direct.End();
            Assert.Equal(appFramebuffer, Query(GL_DRAW_FRAMEBUFFER_BINDING));
        }

        var rgba = GlReadback.ReadTexture(BrutalGlWindow.GetProc, texture, Size, Size);
        // Canvas row 0 is the window-style top, which is the texture's last row.
        int At(int x, int texelRow) => (texelRow * Size + x) * 4;
        Assert.Equal((0, 255, 0), (rgba[At(8, Size - 8)], rgba[At(8, Size - 8) + 1], rgba[At(8, Size - 8) + 2]));
        Assert.Equal((255, 0, 0), (rgba[At(8, 8)], rgba[At(8, 8) + 1], rgba[At(8, 8) + 2]));
    }

    [GlWindowFact]
    public void DirectCanvas_OutsideBeginEnd_Throws()
    {
        using var window = new BrutalGlWindow(64, 64);
        using var context = new SkiaGlContext(BrutalGlWindow.GetProc);
        using var direct = new SkiaDirectCanvas(context);
        Assert.Throws<InvalidOperationException>(() => direct.Canvas);
        Assert.Throws<InvalidOperationException>(direct.End);

        direct.Begin();
        Assert.Throws<InvalidOperationException>(direct.Begin);
        Assert.Throws<InvalidOperationException>(direct.Dispose);
        direct.End();
        Assert.Throws<InvalidOperationException>(() => direct.Canvas);
    }

    static void SaveScreenshot(byte[] rgba)
    {
        var dir = Environment.GetEnvironmentVariable("SKIAGAMERENDERING_BRUTAL_SCREENSHOTS");
        if (string.IsNullOrEmpty(dir))
            return;
        Directory.CreateDirectory(dir);
        using var bitmap = new SKBitmap(new SKImageInfo(GoldenScene.Width, GoldenScene.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, "brutal-ogl-scene.png"), data.ToArray());
    }
}
