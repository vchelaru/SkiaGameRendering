using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform.Graphics;
using SkiaSharp;

namespace Benchmarks.WebGLOptionA;

// Drives the exact 3-step Option A per-frame sequence proven out by the
// worktree-agent-ab7b0bc9d3444d8d7 / worktree-agent-a366b823b701668f9 spikes (repo issue #12), now
// wrapped for repeated timing instead of a single proof-of-concept pass:
//   1. GraphicsDevice.InvalidateStateCache() - the patched KNI call (see Benchmarks.WebGLOptionA.csproj's
//      ProjectReference comment for where the patch lives).
//   2. A Skia draw into KNI's real, shared WebGL2 context (magenta clear + filled AA circle - NOT a
//      bare Clear(), which the second spike found does not exercise the corruption path).
//   3. KNI's own next real draw call (a full-viewport pure-green SpriteBatch quad, reusing the exact
//      SpriteBatch/Texture2D/BlendState.Opaque objects every time - see BenchGame.LoadContent).
[SupportedOSPlatform("browser")]
internal sealed class OptionAFrameRunner
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    private static extern void InterceptBrowserObjects();

    private readonly IJSInProcessObjectReference _module;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly BenchGame _game;
    private readonly GRContext _context;

    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private int _surfaceWidth;
    private int _surfaceHeight;

    public int ContextUid { get; }

    private OptionAFrameRunner(IJSInProcessObjectReference module, BenchGame game, int contextUid, GRContext context)
    {
        _module = module;
        _game = game;
        _graphicsDevice = game.GraphicsDevice;
        ContextUid = contextUid;
        _context = context;
    }

    // Returns null (logging why) instead of throwing, so Index.razor.cs can show a clear
    // "harness did not initialize" status instead of an unhandled WASM exception.
    public static OptionAFrameRunner? Create(IJSInProcessObjectReference module, BenchGame game)
    {
        int contextUid;
        try
        {
            contextUid = GetGlContextUid(game.GraphicsDevice);
            Console.WriteLine($"[optionA] KNI GL context Uid = {contextUid}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[optionA] Failed to resolve KNI's GL context Uid: {exception}");
            return null;
        }

        // Must run BEFORE registerKniContext: this is what installs/populates Skia's Emscripten GL
        // registry (globalThis.SkiaSharpGL) in the first place - production code (SkiaGameWebGlHost)
        // gets this for free because it always creates its own GRContext first. This benchmark
        // deliberately never touches SkiaGameWebGlHost, so it has to call this itself, first.
        try
        {
            InterceptBrowserObjects();
            Console.WriteLine("[optionA] InterceptBrowserObjects() succeeded");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[optionA] InterceptBrowserObjects() threw: {exception}");
            return null;
        }

        var registerResult = module.Invoke<RegisterResult>("registerKniContext", contextUid);
        Console.WriteLine(
            $"[optionA] registerContext available={registerResult.RegisterContextAvailable} " +
            $"success={registerResult.Success} handle={registerResult.Handle} error={registerResult.Error}");
        if (!registerResult.Success)
        {
            Console.WriteLine("[optionA] Aborting: registerContext handoff did not succeed.");
            return null;
        }

        try
        {
            using var glInterface = GRGlInterface.Create()
                ?? throw new InvalidOperationException("GRGlInterface.Create returned null.");
            var context = GRContext.CreateGl(glInterface)
                ?? throw new InvalidOperationException("GRContext.CreateGl returned null.");
            Console.WriteLine("[optionA] GRContext.CreateGl succeeded");
            return new OptionAFrameRunner(module, game, contextUid, context);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[optionA] GRContext creation threw: {exception}");
            return null;
        }
    }

    // The measured sequence. Called synchronously from JS once per benchmarked "frame" (see
    // wwwroot/js/context-bridge.js's runFrames) - NOT gated behind requestAnimationFrame/Present,
    // matching the second spike's approach of driving GraphicsDevice/Skia directly rather than
    // through a full Game.Tick(). Returns the C#-side elapsed milliseconds for steps 1-3 only
    // (excludes the JS<->WASM call boundary - the JS driver separately times the whole
    // invokeMethod round trip for the "frameCpu" column; this is the "innerCpu" column).
    public double RunFrame(int width, int height)
    {
        var stopwatch = Stopwatch.StartNew();

        _graphicsDevice.Viewport = new Viewport(0, 0, width, height);
        _graphicsDevice.InvalidateStateCache();

        EnsureSkiaSurface(width, height);
        using (var paint = new SKPaint { Color = new SKColor(0x00, 0x88, 0xFF, 0xFF), IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            _surface!.Canvas.Clear(new SKColor(0xFF, 0x00, 0xFF, 0xFF)); // pure magenta, whole surface
            _surface.Canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }
        _surface.Flush();
        _context.Flush();

        DrawGreenQuad(width, height);

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    public int[] ReadCenterPixel(int width, int height) =>
        _module.Invoke<int[]>("readKniPixel", ContextUid, width / 2, height / 2);

    private void EnsureSkiaSurface(int width, int height)
    {
        if (_surface != null && _surfaceWidth == width && _surfaceHeight == height)
            return;

        var fbo = _module.Invoke<FramebufferInfo>("getFramebufferInfo", ContextUid);
        _renderTarget?.Dispose();
        _surface?.Dispose();

        var framebufferInfo = new GRGlFramebufferInfo(fbo.FboId, SKColorType.Rgba8888.ToGlSizedFormat());
        _renderTarget = new GRBackendRenderTarget(width, height, fbo.Samples, fbo.Stencils, framebufferInfo);
        _surface = SKSurface.Create(_context, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("SKSurface.Create returned null.");
        _surfaceWidth = width;
        _surfaceHeight = height;
    }

    private void DrawGreenQuad(int width, int height)
    {
        _game.Batch!.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        _game.Batch.Draw(_game.GreenPixel, new Rectangle(0, 0, width, height), Color.White);
        _game.Batch.End();
    }

    // Copied from SkiaGameRendering.Kni.WebGL's KniWebGlInternals.GetGlContextUid (see that file for
    // why this is reflection rather than a public KNI API - repo issue #13). Duplicated locally,
    // same as the spike this is built from, to keep this benchmark fully isolated from production
    // code (src/SkiaGameRendering.Kni.WebGL is never referenced here).
    private static int GetGlContextUid(GraphicsDevice graphicsDevice)
    {
        var currentContext = ((IPlatformGraphicsDevice)graphicsDevice).Strategy.CurrentContext;
        var contextStrategy = ((IPlatformGraphicsContext)currentContext).Strategy;

        var glContextProperty = contextStrategy.GetType().GetProperty("GL", NonPublicInstance)
            ?? throw new MissingMemberException("ConcreteGraphicsContext.GL was not found; KNI's internal layout changed.");

        var glContext = glContextProperty.GetValue(contextStrategy)
            ?? throw new InvalidOperationException("The WebGL context is unavailable.");

        var uidProperty = glContext.GetType().GetProperty("Uid", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException($"{glContext.GetType().Name}.Uid was not found.");
        return (int)uidProperty.GetValue(glContext)!;
    }

    private sealed class RegisterResult
    {
        public bool RegisterContextAvailable { get; set; }
        public bool Success { get; set; }
        public int Handle { get; set; }
        public string? Error { get; set; }
    }

    private sealed class FramebufferInfo
    {
        public uint FboId { get; set; }
        public int Stencils { get; set; }
        public int Samples { get; set; }
        public int Depth { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
