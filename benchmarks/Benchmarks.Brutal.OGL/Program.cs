// Hidden-window OpenGL benchmark: Skia into a SkiaRenderTarget2D texture that the app then draws onto the back buffer
// with a fullscreen triangle, versus SkiaDirectCanvas straight into the back buffer. Two loads (1 rect, 1000 rects) at 1920x1080.
// Usage: Benchmarks.Brutal.OGL [--warmup 30] [--frames 200] [--out results.md]
// Per path it reports: CPU ms/frame (Stopwatch around the frame's Skia work plus, for the texture path, the composite),
// and throughput ms/frame (the whole loop plus glFinish, divided by frames). The window is hidden and never presented.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Brutal.GlfwApi;
using SkiaGameRendering;
using SkiaSharp;

const int W = 1920, H = 1080;
string Get(string name, string fallback)
{
    var i = Array.IndexOf(args, "--" + name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
var warmup = int.Parse(Get("warmup", "30"));
var frames = int.Parse(Get("frames", "200"));
var output = Get("out", "results.md");

if (!Glfw.Init())
{
    Console.WriteLine("glfwInit failed (no display?).");
    return 1;
}
Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 3);
Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 3);
Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, 0x32001 /* GLFW_OPENGL_CORE_PROFILE */);
Glfw.WindowHint(GlfwWindowHint.Visible, 0);
var createInfo = new GlfwWindow.CreateInfo { Title = "Benchmarks.Brutal.OGL", Size = new(W, H) };
using var window = Glfw.CreateWindow(in createInfo);
window.MakeContextCurrent();
Glfw.SwapInterval(0);

static IntPtr GetProc(string name) => Glfw.GetProcAddress(Encoding.ASCII.GetBytes(name + "\0"));
T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(GetProc(name));
var getString = Load<GetString>("glGetString");
string Str(int name) => Marshal.PtrToStringAnsi(getString(name)) ?? "?";
var gpu = $"{Str(0x1F01)} / {Str(0x1F00)}, GL {Str(0x1F02)}";

var viewport = Load<Viewport>("glViewport");
var finish = Load<Action>("glFinish");
var quad = new FullscreenTriangle(new Loader { GetProc = GetProc });
viewport(0, 0, W, H);

var rng = new Random(1);
var rects = Enumerable.Range(0, 1000).Select(_ => (x: rng.Next(0, 3700), y: rng.Next(0, 2000), w: rng.Next(10, 120), h: rng.Next(10, 120), c: new SKColor((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256)))).ToArray();
using var paint = new SKPaint { IsAntialias = true };
void Draw(SKCanvas c, int count)
{
    c.Clear(SKColors.Black);
    foreach (var r in rects.Take(count)) { paint.Color = r.c; c.DrawRect(r.x % (W - 130), r.y % (H - 130), r.w, r.h, paint); }
}

using var context = new SkiaGlContext(GetProc);
var lines = new List<string>
{
    $"GPU: {gpu}. OS: {RuntimeInformation.OSDescription}. Warmup {warmup}, frames {frames}, at {W}x{H}.",
    "",
    "| Load | Path | CPU ms/frame | Throughput ms/frame | CPU, 1st half | CPU, 2nd half |",
    "|---|---|---|---|---|---|",
};

foreach (var count in new[] { 1, 1000 })
{
Measure(count, "SkiaRenderTarget2D + fullscreen draw onto back buffer", () =>
{
    var target = new SkiaRenderTarget2D(context, W, H);
    return (() =>
    {
        target.Begin();
        Draw(target.Canvas, count);
        target.End();
        quad.Draw(W, H, target.GlTexture);
    }, target.Dispose);
});

Measure(count, "SkiaDirectCanvas into back buffer", () =>
{
    var direct = new SkiaDirectCanvas(context);
    return (() =>
    {
        direct.Begin();
        Draw(direct.Canvas, count);
        direct.End();
    }, direct.Dispose);
});

}

File.WriteAllLines(output, lines);
Console.WriteLine($"Wrote {output}");
return 0;

void Measure(int count, string name, Func<(Action frame, Action dispose)> setup)
{
    var (frame, dispose) = setup();
    for (var i = 0; i < warmup; i++)
        frame();
    finish();
    double first = 0, second = 0;
    var total = Stopwatch.GetTimestamp();
    for (var i = 0; i < frames; i++)
    {
        var t0 = Stopwatch.GetTimestamp();
        frame();
        var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (i < frames / 2) first += ms; else second += ms;
    }
    finish();
    var throughput = Stopwatch.GetElapsedTime(total).TotalMilliseconds / frames;
    var line = $"| {(count == 1 ? "1 rect" : "1000 rects")} | {name} | {(first + second) / frames:F3} | {throughput:F3} | {first / (frames / 2):F3} | {second / (frames - frames / 2):F3} |";
    lines.Add(line);
    Console.WriteLine(line);
    dispose();
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Viewport(int x, int y, int w, int h);
[UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr GetString(int name);

/// <summary>Samples a texture over the whole framebuffer with one oversized triangle, as an app composites a Skia texture.</summary>
sealed class FullscreenTriangle
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int CreateShader(int type);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ShaderSource(int shader, int count, IntPtr[] source, int[] length);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Int1(int a);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int CreateProgram();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Int2(int a, int b);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenVertexArrays(int n, out int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void DrawArrays(int mode, int first, int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindFramebuffer(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Viewport(int x, int y, int w, int h);

    readonly Int1 _useProgram, _bindVertexArray, _activeTexture;
    readonly Int2 _bindTexture;
    readonly DrawArrays _drawArrays;
    readonly BindFramebuffer _bindFramebuffer;
    readonly Viewport _viewport;
    readonly int _program, _vao;

    public FullscreenTriangle(Loader load)
    {
        const string vertex = "#version 330 core\nout vec2 uv;\nvoid main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);uv=p;gl_Position=vec4(p*2.0-1.0,0.0,1.0);}";
        const string fragment = "#version 330 core\nin vec2 uv;\nout vec4 color;\nuniform sampler2D tex;\nvoid main(){color=texture(tex,uv);}";
        int Compile(int type, string source)
        {
            var shader = load.Invoke<CreateShader>("glCreateShader")(type);
            var text = Marshal.StringToHGlobalAnsi(source);
            load.Invoke<ShaderSource>("glShaderSource")(shader, 1, [text], [source.Length]);
            load.Invoke<Int1>("glCompileShader")(shader);
            return shader;
        }
        var vs = Compile(0x8B31, vertex);
        var fs = Compile(0x8B30, fragment);
        _program = load.Invoke<CreateProgram>("glCreateProgram")();
        load.Invoke<Int2>("glAttachShader")(_program, vs);
        load.Invoke<Int2>("glAttachShader")(_program, fs);
        load.Invoke<Int1>("glLinkProgram")(_program);
        load.Invoke<GenVertexArrays>("glGenVertexArrays")(1, out _vao);
        _useProgram = load.Invoke<Int1>("glUseProgram");
        _bindVertexArray = load.Invoke<Int1>("glBindVertexArray");
        _activeTexture = load.Invoke<Int1>("glActiveTexture");
        _bindTexture = load.Invoke<Int2>("glBindTexture");
        _drawArrays = load.Invoke<DrawArrays>("glDrawArrays");
        _bindFramebuffer = load.Invoke<BindFramebuffer>("glBindFramebuffer");
        _viewport = load.Invoke<Viewport>("glViewport");
    }

    public void Draw(int width, int height, int texture)
    {
        // Skia leaves its own GL state behind, so the app sets everything it needs again.
        _bindFramebuffer(0x8D40, 0);
        _viewport(0, 0, width, height);
        _useProgram(_program);
        _bindVertexArray(_vao);
        _activeTexture(0x84C0);
        _bindTexture(0x0DE1, texture);
        _drawArrays(0x0004, 0, 3);
    }
}

sealed class Loader
{
    public required Func<string, IntPtr> GetProc { get; init; }
    public T Invoke<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(GetProc(name));
}
