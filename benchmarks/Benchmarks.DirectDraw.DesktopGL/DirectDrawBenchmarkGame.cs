using System.Diagnostics;
using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;

namespace Benchmarks.DirectDraw;

/// <summary>
/// Draws <see cref="BenchmarkScene"/> every frame, either into a <see cref="SkiaRenderTarget2D"/> that
/// SpriteBatch composites onto the back buffer ("offscreen", the old path) or straight onto the back buffer
/// with <see cref="SkiaRenderer.DrawToCurrentTarget"/> ("direct"). "ab" alternates the two in blocks of
/// <see cref="BlockFrames"/> frames inside one process, so GPU clock and power-state drift lands on both
/// alike. Vsync and fixed time step are off. After the warmup frames it records, per mode, the
/// frame-to-frame interval, the CPU time of the Skia section, and the GPU span of the frame, then appends
/// one result line per mode and exits. The window stays hidden (see <see cref="RunHidden"/>).
/// </summary>
sealed class DirectDrawBenchmarkGame : Game
{
    const int BlockFrames = 50;
    const int GpuLag = 3;

    sealed class Samples
    {
        internal readonly List<double> IntervalMs = new();
        internal readonly List<double> SkiaCpuMs = new();
        internal readonly List<double> GpuMs = new();
    }

    readonly BenchmarkOptions _options;
    readonly Dictionary<string, Samples> _samples = new() { ["offscreen"] = new(), ["direct"] = new() };
    readonly string[] _frameModes = new string[GpuLag + 1];
    BenchmarkScene _scene = null!;
    SkiaRenderTarget2D? _offscreen;
    GpuTimestamps _gpu = null!;
    Action<SKCanvas> _drawScene = null!;
    long _lastFrame;
    string? _lastMode;
    int _frame;

    internal DirectDrawBenchmarkGame(BenchmarkOptions options)
    {
        _options = options;
        new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = options.Width,
            PreferredBackBufferHeight = options.Height,
            SynchronizeWithVerticalRetrace = false,
            PreferMultiSampling = false,
        };
        IsFixedTimeStep = false;
        // A hidden window is never active, and Tick sleeps this long every inactive frame (20 ms by default).
        InactiveSleepTime = TimeSpan.Zero;
        Window.Title = $"DirectDraw benchmark - {options.Mode} {options.Width}x{options.Height}";
    }

    internal bool Finished { get; private set; }

    [System.Runtime.InteropServices.DllImport("SDL2", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    static extern uint SDL_GetWindowFlags(IntPtr window);

    const uint SDL_WINDOW_SHOWN = 0x4;

    /// <summary>
    /// Runs every frame through <see cref="Game.RunOneFrame"/> instead of <see cref="Game.Run()"/>. MonoGame
    /// creates the SDL window hidden and only shows it inside <c>Run</c>'s loop, so this keeps the window
    /// off screen while its back buffer still renders at full size.
    /// </summary>
    internal void RunHidden()
    {
        while (!Finished)
        {
            RunOneFrame();
            if ((SDL_GetWindowFlags(Window.Handle) & SDL_WINDOW_SHOWN) != 0)
                throw new InvalidOperationException("The benchmark window became visible.");
        }
    }

    string ModeOf(int frame) => _options.Mode != "ab" ? _options.Mode
        : (frame / BlockFrames) % 2 == 0 ? "offscreen" : "direct";

    protected override void LoadContent()
    {
        SkiaRenderer.Initialize(GraphicsDevice);
        _scene = new BenchmarkScene(_options.Width, _options.Height);
        _drawScene = canvas => _scene.Draw(canvas, _frame);
        if (_options.Mode != "direct")
            _offscreen = new SkiaRenderTarget2D(GraphicsDevice, _options.Width, _options.Height);
        _gpu = new GpuTimestamps();
    }

    protected override void Draw(GameTime gameTime)
    {
        var mode = ModeOf(_frame);
        var measuring = _frame >= _options.Warmup;

        // The interval since the last Draw is the previous frame's cost, Present included.
        var now = Stopwatch.GetTimestamp();
        if (measuring && _lastFrame != 0 && _lastMode == mode)
            _samples[_lastMode].IntervalMs.Add(Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds);
        _lastFrame = now;
        _lastMode = mode;

        _frameModes[_frame % _frameModes.Length] = mode;
        _gpu.Begin(_frame);
        GraphicsDevice.Clear(Color.CornflowerBlue);

        var skiaStart = Stopwatch.GetTimestamp();
        if (mode == "offscreen")
        {
            _offscreen!.Begin();
            _drawScene(_offscreen.Canvas);
            _offscreen.End();
        }
        else
        {
            SkiaRenderer.DrawToCurrentTarget(GraphicsDevice, _drawScene);
        }
        var skiaMs = Stopwatch.GetElapsedTime(skiaStart).TotalMilliseconds;
        _gpu.End(_frame);

        if (measuring)
            _samples[mode].SkiaCpuMs.Add(skiaMs);
        var gpuMs = _gpu.ReadLagged(_frame);
        if (gpuMs != null && _frame - GpuLag >= _options.Warmup)
            _samples[_frameModes[(_frame - GpuLag) % _frameModes.Length]].GpuMs.Add(gpuMs.Value);

        _frame++;
        if (_frame >= _options.Warmup + _options.Frames)
        {
            if (_options.Capture != null)
            {
                // Raw RGBA of the last frame, to diff the two modes' output against each other.
                var pixels = new byte[_options.Width * _options.Height * 4];
                GraphicsDevice.GetBackBufferData(pixels);
                File.WriteAllBytes(_options.Capture, pixels);
            }
            WriteResults();
            Finished = true;
        }

        base.Draw(gameTime);
    }

    void WriteResults()
    {
        var renderer = GpuTimestamps.Renderer();
        foreach (var (mode, samples) in _samples)
        {
            if (samples.SkiaCpuMs.Count == 0)
                continue;

            var line = string.Create(CultureInfo.InvariantCulture,
                $"| {renderer} | {mode} | {_options.Width}x{_options.Height} | {_options.Run} | " +
                $"{Median(samples.IntervalMs):0.000} | {Percentile(samples.IntervalMs, 0.95):0.000} | " +
                $"{Median(samples.SkiaCpuMs):0.000} | {Percentile(samples.SkiaCpuMs, 0.95):0.000} | " +
                $"{Median(samples.GpuMs):0.000} | {Percentile(samples.GpuMs, 0.95):0.000} | {samples.SkiaCpuMs.Count} |");
            File.AppendAllText(_options.Output, line + Environment.NewLine);
            Console.WriteLine(line);
        }
    }

    static double Median(List<double> values) => Percentile(values, 0.5);

    static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
            return double.NaN;
        var sorted = values.Order().ToArray();
        return sorted[(int)Math.Min(sorted.Length - 1, Math.Round(percentile * (sorted.Length - 1)))];
    }

    protected override void UnloadContent()
    {
        _offscreen?.Dispose();
        _scene.Dispose();
        SkiaRenderer.Dispose();
        base.UnloadContent();
    }
}
