// BRUTAL + SkiaGameRendering.Brutal.OGL. Opens a hidden GLFW window with an OpenGL 3.3 core context, draws with Skia
// into a GL texture, and with --smoke-test reads ONE pixel back with glReadPixels to check it (exit 0 = pass).
// The readback is test-only verification; the library has no CPU readback path.
// With no flags it opens a visible window that redraws the shared Scene every frame.
// --screenshot <path> instead draws the shared Scene on its background at 800x800 and saves the Skia target to a PNG.
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using SampleShared;
using SkiaGameRendering;
using SkiaSharp;

const int Size = 64;
var shotAt = Array.IndexOf(args, "--screenshot");
var shotPath = shotAt >= 0 && shotAt + 1 < args.Length ? args[shotAt + 1] : null;
var smokeTest = args.Contains("--smoke-test");

if (!Glfw.Init())
{
    Console.WriteLine("glfwInit failed (no display?).");
    return 1;
}

Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 3);
Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 3);
Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, 0x32001 /* GLFW_OPENGL_CORE_PROFILE */);
var interactive = shotPath == null && !smokeTest;
Glfw.WindowHint(GlfwWindowHint.Visible, interactive ? 1 : 0);
var createInfo = new GlfwWindow.CreateInfo { Title = "Sample.Brutal.OGL", Size = new(Scene.Width, Scene.Height) };
using var window = Glfw.CreateWindow(in createInfo);
window.MakeContextCurrent();

static IntPtr GetProc(string name) => Glfw.GetProcAddress(System.Text.Encoding.ASCII.GetBytes(name + "\0"));

using var context = new SkiaGlContext(GetProc);
var exitCode = 0;

if (interactive)
{
    // The shared Scene redrawn every frame into a Skia texture, then blitted to the window (flipped, since GL's
    // window row 0 is the bottom). Close the window to exit.
    using var scene = new SkiaRenderTarget2D(context, Scene.Width, Scene.Height);
    var blit = new GlBlit(GetProc, scene.GlTexture);
    Glfw.SwapInterval(1);
    while (!window.ShouldClose)
    {
        Glfw.PollEvents();
        scene.Begin();
        scene.Canvas.Clear(Scene.Background);
        Scene.Draw(scene.Canvas);
        scene.End();
        blit.ToWindow(Scene.Width, Scene.Height, window.FramebufferSize.X, window.FramebufferSize.Y);
        window.SwapBuffers();
    }
    blit.Dispose();
    return 0;
}

if (shotPath != null)
{
    using var scene = new SkiaRenderTarget2D(context, Scene.Width, Scene.Height);
    scene.Begin();
    scene.Canvas.Clear(Scene.Background);
    Scene.Draw(scene.Canvas);
    scene.End();
    var rgba = GlReadback.ReadTexture(GetProc, scene.GlTexture, Scene.Width, Scene.Height);
    using var bitmap = new SKBitmap(new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
    using var png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(shotPath, png.ToArray());
    return 0;
}

using (var target = new SkiaRenderTarget2D(context, Size, Size))
{
    // Two frames, so the second checks that the app's GL state changing between passes does not break Skia.
    target.Begin();
    target.Canvas.Clear(SKColors.Green);
    target.End();

    target.Begin();
    target.Canvas.Clear(new SKColor(30, 144, 255));
    using (var paint = new SKPaint { Color = new SKColor(255, 0, 0), IsAntialias = true })
        target.Canvas.DrawRect(16, 16, 32, 32, paint);
    target.End();

    var rgba = GlReadback.ReadTexture(GetProc, target.GlTexture, Size, Size);
    var i = (Size / 2 * Size + Size / 2) * 4;
    Console.WriteLine($"pixel ({Size / 2},{Size / 2}) = {rgba[i]},{rgba[i + 1]},{rgba[i + 2]},{rgba[i + 3]} (expected 255,0,0,255)");
    if (!smokeTest)
        Console.WriteLine("Pass --smoke-test to assert it.");
    else
    {
        exitCode = rgba[i] == 255 && rgba[i + 1] == 0 && rgba[i + 2] == 0 && rgba[i + 3] == 255 ? 0 : 1;
        Console.WriteLine(exitCode == 0 ? "PASS" : "FAIL");
    }
}
return exitCode;
