// The performance suite on BRUTAL OpenGL: a GLFW window with an OpenGL 3.3 core context, vsync off. Each frame draws
// the entry's scene into a window-sized SkiaRenderTarget2D, blits it to the back buffer (as samples/Sample.Brutal.OGL
// does) and swaps. Same arguments and results file as the MonoGame and KNI apps (see PerfOptions).
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Performance;
using SkiaGameRendering;
using SkiaSharp;

var options = PerfOptions.Parse(args);

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
var host = new BrutalGlHost(window, context, options.Antialias, options.MultiSampleCount);
var runner = new PerfRunner("BRUTAL OpenGL Skia", options, host);
runner.Start(scene => SkiaEntries.For(scene, direct: false, targetSizes: true));
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
    readonly SkiaGlContext _context;
    readonly SkiaRenderTarget2D _target;
    readonly GlBlit _blit;
    readonly SkiaSceneDrawer _drawer;
    readonly Func<string, IntPtr> _getProc;

    readonly int _multiSampleCount;

    public BrutalGlHost(GlfwWindow window, SkiaGlContext context, bool antialias, int multiSampleCount)
    {
        _window = window;
        _context = context;
        _multiSampleCount = multiSampleCount;
        _getProc = name => Glfw.GetProcAddress(System.Text.Encoding.ASCII.GetBytes(name + "\0"));
        _target = new SkiaRenderTarget2D(context, PerfRunner.Width, PerfRunner.Height, multiSampleCount: multiSampleCount);
        _blit = new GlBlit(_getProc, _target.GlTexture);
        _drawer = new SkiaSceneDrawer(antialias);
    }

    // Render targets for the target-size guide scenes, made in PrepareEntry so creating them is not measured.
    readonly Dictionary<(int Width, int Height), (SkiaRenderTarget2D Target, GlBlit Blit)> _sizedTargets = new();

    public void PrepareEntry(PerfEntry entry)
    {
        var size = (entry.Scene.TargetWidth, entry.Scene.TargetHeight);
        if (size.TargetWidth > 0 && !_sizedTargets.ContainsKey(size))
        {
            var target = new SkiaRenderTarget2D(_context, size.TargetWidth, size.TargetHeight, multiSampleCount: _multiSampleCount);
            _sizedTargets[size] = (target, new GlBlit(_getProc, target.GlTexture));
        }
    }

    public void DrawEntry(PerfEntry entry, float t)
    {
        // A target-size scene draws into a target of its own size, which the blit scales to the window.
        var (target, blit) = entry.Scene.TargetWidth > 0
            ? _sizedTargets[(entry.Scene.TargetWidth, entry.Scene.TargetHeight)]
            : (_target, _blit);
        target.Begin(clear: false);
        try
        {
            // The blit copies rather than blends, so black here matches the other apps' black clear behind the composite.
            target.Canvas.Clear(SKColors.Black);
            // Uploads need Skia's GPU context current, which it only is between Begin and End.
            _drawer.Prepare((GRContext)target.Canvas.Context!, entry.Scene);
            _drawer.Draw(target.Canvas, entry.Scene, t, SkiaEntries.VariantOf(entry));
        }
        finally
        {
            target.End();
        }
        blit.ToWindow(target.Width, target.Height, _window.FramebufferSize.X, _window.FramebufferSize.Y);
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

    public string MsaaDescription => _multiSampleCount > 0 ? $"{_multiSampleCount}x on the render target" : "off";

    public void Dispose()
    {
        _drawer.Dispose();
        _blit.Dispose();
        _target.Dispose();
        foreach (var (target, blit) in _sizedTargets.Values)
        {
            blit.Dispose();
            target.Dispose();
        }
    }
}
