// The performance suite on BRUTAL OpenGL: a GLFW window with an OpenGL 3.3 core context, vsync off. Each frame draws
// the entry's scene into a window-sized SkiaRenderTarget2D, blits it to the back buffer (as samples/Sample.Brutal.OGL
// does) and swaps. Same arguments and results file as the MonoGame and KNI apps (see PerfOptions).
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Performance;
using SkiaGameRendering;
using SkiaSharp;

var options = PerfOptions.Parse(args);
if (options.MultiSampleCount > 0)
{
    // TODO: pass options.MultiSampleCount to SkiaRenderTarget2D once BRUTAL OpenGL's takes a sample count.
    Console.Error.WriteLine("--msaa is not supported on BRUTAL OpenGL yet: its SkiaRenderTarget2D has no sample count.");
    return 2;
}

if (!Glfw.Init())
{
    Console.Error.WriteLine("glfwInit failed (no display?).");
    return 1;
}

Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 3);
Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 3);
Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, 0x32001 /* GLFW_OPENGL_CORE_PROFILE */);
Glfw.WindowHint(GlfwWindowHint.Resizable, 0);
var createInfo = new GlfwWindow.CreateInfo { Title = "BRUTAL OpenGL Skia", Size = new(PerfRunner.Width, PerfRunner.Height) };
using var window = Glfw.CreateWindow(in createInfo);
window.MakeContextCurrent();
Glfw.SwapInterval(0);

static IntPtr GetProc(string name) => Glfw.GetProcAddress(System.Text.Encoding.ASCII.GetBytes(name + "\0"));

using var context = new SkiaGlContext(GetProc);
var host = new BrutalGlHost(window, context, options.Antialias);
var runner = new PerfRunner("BRUTAL OpenGL Skia", options, host);
runner.Start(scene => SkiaEntries.For(scene, direct: false));
while (!runner.Finished && !window.ShouldClose)
{
    Glfw.PollEvents();
    runner.Frame();
    window.SwapBuffers();
}
host.Dispose();
return 0;

sealed class BrutalGlHost : IPerfHost, IDisposable
{
    const uint GL_RENDERER = 0x1F01;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetString(uint name);

    readonly GlfwWindow _window;
    readonly SkiaRenderTarget2D _target;
    readonly GlBlit _blit;
    readonly SkiaSceneDrawer _drawer;
    readonly Func<string, IntPtr> _getProc;

    public BrutalGlHost(GlfwWindow window, SkiaGlContext context, bool antialias)
    {
        _window = window;
        _getProc = name => Glfw.GetProcAddress(System.Text.Encoding.ASCII.GetBytes(name + "\0"));
        _target = new SkiaRenderTarget2D(context, PerfRunner.Width, PerfRunner.Height);
        _blit = new GlBlit(_getProc, _target.GlTexture);
        _drawer = new SkiaSceneDrawer(antialias);
    }

    public void PrepareEntry(PerfEntry entry) { }

    public void DrawEntry(PerfEntry entry, float t)
    {
        _target.Begin(clear: false);
        try
        {
            // The blit copies rather than blends, so black here matches the other apps' black clear behind the composite.
            _target.Canvas.Clear(SKColors.Black);
            // Uploads need Skia's GPU context current, which it only is between Begin and End.
            _drawer.Prepare((GRContext)_target.Canvas.Context!,entry.Scene);
            _drawer.Draw(_target.Canvas, entry.Scene, t, SkiaEntries.VariantOf(entry));
        }
        finally
        {
            _target.End();
        }
        _blit.ToWindow(PerfRunner.Width, PerfRunner.Height, _window.FramebufferSize.X, _window.FramebufferSize.Y);
    }

    public void SaveScreenshot(PerfEntry entry, string path)
    {
        var rgba = GlReadback.ReadDefaultFramebuffer(_getProc, PerfRunner.Width, PerfRunner.Height);
        using var bitmap = new SKBitmap(new SKImageInfo(PerfRunner.Width, PerfRunner.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
    }

    public bool IsActive => _window.IsFocused;

    public void SetTitle(string title) => _window.Title = title;

    public string GpuDescription =>
        Marshal.PtrToStringAnsi(Marshal.GetDelegateForFunctionPointer<GetString>(_getProc("glGetString"))(GL_RENDERER)) ?? "unknown";

    public string MsaaDescription => "off";

    public void Dispose()
    {
        _drawer.Dispose();
        _blit.Dispose();
        _target.Dispose();
    }
}
