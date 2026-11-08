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

    readonly ITestOutputHelper _output;

    public BrutalOglTests(ITestOutputHelper output) => _output = output;

    [Fact]
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

    [Fact]
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
