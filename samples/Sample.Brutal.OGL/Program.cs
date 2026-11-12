// BRUTAL + SkiaGameRendering.Brutal.OGL. Opens a hidden GLFW window with an OpenGL 3.3 core context, draws with Skia
// into a GL texture, and with --smoke-test reads ONE pixel back with glReadPixels to check it (exit 0 = pass).
// The readback is test-only verification; the library has no CPU readback path.
// With no flags it opens a visible window that redraws the shared Scene every frame.
// --screenshot <path> instead draws the shared Scene on its background at 800x800 and saves the Skia target to a PNG.
// --direct (interactive) draws the Scene with SkiaDirectCanvas straight into the window, with no texture and no blit.
// --smoke-test also draws the Scene through SkiaDirectCanvas into the default framebuffer and compares it to the texture render.
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
static void SetViewport(int width, int height) =>
    Marshal.GetDelegateForFunctionPointer<Viewport>(GetProc("glViewport"))(0, 0, width, height);

using var context = new SkiaGlContext(GetProc);
var exitCode = 0;

if (interactive && args.Contains("--direct"))
{
    using var direct = new SkiaDirectCanvas(context);
    Glfw.SwapInterval(1);
    while (!window.ShouldClose)
    {
        Glfw.PollEvents();
        var size = window.FramebufferSize;
        SetViewport(size.X, size.Y);
        direct.Begin();
        direct.Canvas.Clear(Scene.Background);
        // Scene is 800x800; scale it to the window like the texture path's blit does.
        direct.Canvas.Scale(size.X / (float)Scene.Width, size.Y / (float)Scene.Height);
        Scene.Draw(direct.Canvas);
        direct.End();
        window.SwapBuffers();
    }
    return 0;
}

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

if (smokeTest && exitCode == 0)
{
    // The same Scene through the texture and straight into the default framebuffer must agree, which also
    // checks the direct canvas is not flipped.
    using var scene = new SkiaRenderTarget2D(context, Scene.Width, Scene.Height);
    scene.Begin();
    scene.Canvas.Clear(Scene.Background);
    Scene.Draw(scene.Canvas);
    scene.End();
    var viaTexture = GlReadback.ReadTexture(GetProc, scene.GlTexture, Scene.Width, Scene.Height);
    int Offset(int x, int y) => (y * Scene.Width + x) * 4;
    var clipped = Offset(Scene.ClipCenter, Scene.ClipCenter);
    var clippedOut = Offset(Scene.ClippedOutX, Scene.ClippedOutY);
    var clipPass = viaTexture[clipped] > 200 && viaTexture[clipped + 1] > 120 && viaTexture[clipped + 2] < 80
        && viaTexture[clippedOut] == Scene.Background.Red && viaTexture[clippedOut + 1] == Scene.Background.Green
        && viaTexture[clippedOut + 2] == Scene.Background.Blue;
    Console.WriteLine($"antialiased clip: inside {viaTexture[clipped]},{viaTexture[clipped + 1]},{viaTexture[clipped + 2]} " +
        $"outside {viaTexture[clippedOut]},{viaTexture[clippedOut + 1]},{viaTexture[clippedOut + 2]} -> {(clipPass ? "PASS" : "FAIL")}");

    // The Scene's last draw leaves Skia's scissor test on; the blit honors it, so GlBlit has to turn it off or it
    // copies only the clip's rectangle.
    using (var blit = new GlBlit(GetProc, scene.GlTexture))
        blit.ToWindow(Scene.Width, Scene.Height, Scene.Width, Scene.Height);
    var viaBlit = GlReadback.ReadDefaultFramebuffer(GetProc, Scene.Width, Scene.Height);
    var blitDiffering = CountDiffering(viaTexture, viaBlit);
    var blitPass = blitDiffering == 0;
    Console.WriteLine($"blit to back buffer vs texture: {blitDiffering} pixels differ -> {(blitPass ? "PASS" : "FAIL")}");
    if (!clipPass || !blitPass)
        exitCode = 1;

    using var direct = new SkiaDirectCanvas(context);
    byte[] viaBackBuffer = [];
    // Twice: the second pass reuses the cached back buffer surface.
    for (var pass = 0; pass < 2; pass++)
    {
        // Begin takes the size from the viewport, which is undefined after a pass.
        SetViewport(Scene.Width, Scene.Height);
        direct.Begin();
        direct.Canvas.Clear(Scene.Background);
        Scene.Draw(direct.Canvas);
        direct.End();
        viaBackBuffer = GlReadback.ReadDefaultFramebuffer(GetProc, Scene.Width, Scene.Height);
    }

    // Both readbacks are top row first.
    var differing = CountDiffering(viaTexture, viaBackBuffer);
    var fraction = differing / (double)(Scene.Width * Scene.Height);
    var directPass = fraction < 0.005;
    Console.WriteLine($"direct canvas vs texture: {differing} pixels differ ({fraction:P3}) -> {(directPass ? "PASS" : "FAIL")}");
    if (!directPass)
        exitCode = 1;
}
return exitCode;

// Pixels whose RGB differs by more than 4 in any channel.
static int CountDiffering(byte[] a, byte[] b)
{
    var differing = 0;
    for (var p = 0; p < a.Length / 4; p++)
        for (var c = 0; c < 3; c++)
            if (Math.Abs(a[p * 4 + c] - b[p * 4 + c]) > 4)
            {
                differing++;
                break;
            }
    return differing;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate void Viewport(int x, int y, int width, int height);
