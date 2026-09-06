using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform.Graphics;
using SkiaSharp;

namespace Benchmarks.WebGLOptionA;

// Live, in-page counterpart to OptionAFrameRunner: this repo's ACTUAL shipped architecture
// (src/SkiaGameRendering.Kni.WebGL) rather than the proposed alternative - Skia renders to its OWN
// separate canvas/context (wwwroot/js/option-d-live.js's createOptionDContext, mirroring production
// skia-game-webgl.js's createContext()), then a cross-context texSubImage2D blit copies the result
// into a dedicated KNI destination Texture2D (uploadOptionDCanvasToKniTexture, mirroring production
// WebGlCanvasUpload.cs's uploadFromCanvas). Added so both architectures can be measured back-to-back
// in the SAME page load/browser session/power state as Option A - see repo issue #12.
public sealed class OptionDFrameTiming
{
    public double SkiaDrawMs { get; set; }
    // The number directly comparable to Option A's InteropOverheadMs and to
    // docs/webgl/performance-results.md's uploadCpu - the cross-context blit only, excluding
    // Skia's own render time (SkiaDrawMs above) and KNI's own subsequent draw (KniDrawMs below).
    public double UploadMs { get; set; }
    public double KniDrawMs { get; set; }
    public double TotalMs { get; set; }
}

[SupportedOSPlatform("browser")]
internal sealed class OptionDFrameRunner
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    internal const string CanvasElementId = "option-d-source-canvas";

    private readonly IJSInProcessObjectReference _module;
    private readonly BenchGame _game;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly GRContext _context;
    private readonly int _kniContextUid;
    private readonly int _glContextHandle;

    private Texture2D? _destinationTexture;
    private int _destinationTextureUid;
    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private int _surfaceWidth;
    private int _surfaceHeight;

    private OptionDFrameRunner(
        IJSInProcessObjectReference module, BenchGame game, GRContext context, int kniContextUid, int glContextHandle)
    {
        _module = module;
        _game = game;
        _graphicsDevice = game.GraphicsDevice;
        _context = context;
        _kniContextUid = kniContextUid;
        _glContextHandle = glContextHandle;
    }

    // Returns null (logging why) instead of throwing - Index.razor.cs degrades to Option-A-only
    // rather than failing the whole page if Option D's live path can't initialize.
    public static OptionDFrameRunner? Create(IJSInProcessObjectReference module, BenchGame game, int kniContextUid)
    {
        try
        {
            var fbo = module.Invoke<FramebufferInfo>("createOptionDContext", CanvasElementId);
            Console.WriteLine($"[optionD] createOptionDContext succeeded, handle={fbo.Handle} fboId={fbo.FboId}");

            using var glInterface = GRGlInterface.Create()
                ?? throw new InvalidOperationException("GRGlInterface.Create returned null for Option D's dedicated context.");
            var context = GRContext.CreateGl(glInterface)
                ?? throw new InvalidOperationException("GRContext.CreateGl returned null for Option D's dedicated context.");
            Console.WriteLine("[optionD] GRContext.CreateGl succeeded");

            return new OptionDFrameRunner(module, game, context, kniContextUid, fbo.Handle);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[optionD] Create failed: {exception}");
            return null;
        }
    }

    // The measured sequence, matching this repo's real production per-frame flow: Skia draws to its
    // own canvas, the result is blitted into a KNI texture, KNI draws that texture. Same visual
    // content as OptionAFrameRunner.RunFrame's Skia draw (magenta clear + filled AA circle) so the
    // two architectures differ ONLY in interop mechanism, not in what's being rendered.
    public OptionDFrameTiming RunFrame(int width, int height)
    {
        _graphicsDevice.Viewport = new Viewport(0, 0, width, height);

        var skiaWatch = Stopwatch.StartNew();
        _module.InvokeVoid("makeGlContextCurrent", _glContextHandle);
        EnsureSkiaSurface(width, height);
        using (var paint = new SKPaint { Color = new SKColor(0x00, 0x88, 0xFF, 0xFF), IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            _surface!.Canvas.Clear(new SKColor(0xFF, 0x00, 0xFF, 0xFF)); // pure magenta, whole surface
            _surface.Canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }
        _surface.Flush();
        _context.Flush();
        var skiaDrawMs = skiaWatch.Elapsed.TotalMilliseconds;

        EnsureDestinationTexture(width, height);
        var uploadWatch = Stopwatch.StartNew();
        _module.InvokeVoid(
            "uploadOptionDCanvasToKniTexture",
            _kniContextUid, _destinationTextureUid, CanvasElementId,
            /* flipY */ true, /* premultiplyAlpha */ true, /* disableColorSpaceConversion */ false, /* useTexImage */ false);
        // Same production dirty-marking as WebGlCanvasUpload.cs's KniWebGlInternals.Upload - binding
        // behind KNI's back invalidates its cached notion of what's bound to texture unit 0.
        ((IPlatformTextureCollection)_graphicsDevice.VertexTextures).Strategy.Dirty(0);
        ((IPlatformTextureCollection)_graphicsDevice.Textures).Strategy.Dirty(0);
        var uploadMs = uploadWatch.Elapsed.TotalMilliseconds;

        var kniWatch = Stopwatch.StartNew();
        _game.Batch!.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        _game.Batch.Draw(_destinationTexture, new Rectangle(0, 0, width, height), Color.White);
        _game.Batch.End();
        var kniDrawMs = kniWatch.Elapsed.TotalMilliseconds;

        return new OptionDFrameTiming
        {
            SkiaDrawMs = skiaDrawMs,
            UploadMs = uploadMs,
            KniDrawMs = kniDrawMs,
            TotalMs = skiaDrawMs + uploadMs + kniDrawMs,
        };
    }

    private void EnsureSkiaSurface(int width, int height)
    {
        if (_surface != null && _surfaceWidth == width && _surfaceHeight == height)
            return;

        var fbo = _module.Invoke<FramebufferInfo>("getOptionDFramebufferInfo");
        _renderTarget?.Dispose();
        _surface?.Dispose();

        var framebufferInfo = new GRGlFramebufferInfo(fbo.FboId, SKColorType.Rgba8888.ToGlSizedFormat());
        _renderTarget = new GRBackendRenderTarget(width, height, fbo.Samples, fbo.Stencils, framebufferInfo);
        _surface = SKSurface.Create(_context, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("SKSurface.Create returned null for Option D's dedicated context.");
        _surfaceWidth = width;
        _surfaceHeight = height;
    }

    private void EnsureDestinationTexture(int width, int height)
    {
        if (_destinationTexture != null && _destinationTexture.Width == width && _destinationTexture.Height == height)
            return;

        _destinationTexture?.Dispose();
        _destinationTexture = new Texture2D(_graphicsDevice, width, height, false, SurfaceFormat.Color);
        _destinationTextureUid = GetGlTextureUid(_destinationTexture);
    }

    // Same reflection this benchmark's OptionAFrameRunner/production WebGlCanvasUpload.cs already
    // rely on - KNI has no public accessor for a Texture2D's underlying WebGLTexture handle
    // (repo issue #13). Duplicated locally rather than shared, matching this benchmark's existing
    // "stay fully isolated from production code" convention.
    private static int GetGlTextureUid(Texture2D texture)
    {
        var strategyField = typeof(Texture2D).GetField("_strategyTexture2D", NonPublicInstance)
            ?? throw new MissingFieldException("Texture2D._strategyTexture2D was not found; KNI's internal layout changed.");
        var strategy = strategyField.GetValue(texture)
            ?? throw new InvalidOperationException("Texture2D has no backing strategy.");
        var glTextureField = strategy.GetType().GetField("_glTexture", NonPublicInstance)
            ?? throw new MissingFieldException("ConcreteTexture2D._glTexture was not found; KNI's internal layout changed.");
        var glTexture = glTextureField.GetValue(strategy)
            ?? throw new ObjectDisposedException(nameof(texture));
        var uidProperty = glTexture.GetType().GetProperty("Uid", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException($"{glTexture.GetType().Name}.Uid was not found.");
        return (int)uidProperty.GetValue(glTexture)!;
    }

    private sealed class FramebufferInfo
    {
        public int Handle { get; set; }
        public uint FboId { get; set; }
        public int Stencils { get; set; }
        public int Samples { get; set; }
        public int Depth { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
