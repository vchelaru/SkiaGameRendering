using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Kni.WebGL.Components;
using SkiaSharp;

namespace SkiaGameRendering.Kni.WebGL;

[SupportedOSPlatform("browser")]
public sealed class SkiaWebGlBackend : SkiaBackend
{
    private readonly SkiaGameWebGlHost _host;
    private readonly SkiaWebGlOptions _options;
    private int _ownerThreadId;
    private bool _isDisposed;
    private int _sourceWidth;
    private int _sourceHeight;
    private long _renderStart;

    public SkiaWebGlBackend(SkiaGameWebGlHost host, SkiaWebGlOptions? options = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new SkiaWebGlOptions();
        Diagnostics = new SkiaWebGlDiagnostics();
        _host.ContextLost += OnContextLost;
        _host.ContextRestored += OnContextRestored;
    }

    public SkiaWebGlDiagnostics Diagnostics { get; }
    public override GRContext GRContext => _host.GRContext;

    public override void Initialize(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        if (!_host.IsReady)
            throw new InvalidOperationException("Await the SkiaGameWebGlHost.Ready task before initializing the backend.");
        if (_options.RequireWebGl2 && !_host.WebGlVersion.Contains("WebGL 2", StringComparison.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException("WebGL 2 is required by this backend configuration.");
        if (_options.ResourceCacheBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(_options.ResourceCacheBytes));

        _host.ConfigureResourceCache(_options.ResourceCacheBytes);
        GraphicsDevice = graphicsDevice;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal override void BeginDraw() => EnsureThread();
    internal override void EndDraw() { }

    internal override SkiaTarget CreateTarget(int width, int height, SKColorType colorType)
    {
        EnsureThread();
        var format = ToSurfaceFormat(colorType);
        if (format != SurfaceFormat.Color)
            throw new NotSupportedException("The WebGL backend supports SurfaceFormat.Color only.");

        Diagnostics.TargetCreationCount++;
        return new SkiaWebGlTarget(new Texture2D(GraphicsDevice, width, height, false, format));
    }

    internal override SKCanvas BeginRender(SkiaTarget target, bool clear)
    {
        EnsureThread();
        if (target is not SkiaWebGlTarget)
            throw new ArgumentException("The target was not created by this WebGL backend.", nameof(target));

        _renderStart = Stopwatch.GetTimestamp();
        var width = target.Texture.Width;
        var height = target.Texture.Height;

        if (_sourceWidth != width || _sourceHeight != height)
        {
            if (_sourceWidth > 0 && _sourceHeight > 0)
                Diagnostics.ResizeCount++;
            _sourceWidth = width;
            _sourceHeight = height;
        }

        SKCanvas canvas;
        try
        {
            canvas = _host.BeginRenderNow(width, height);
        }
        catch (Exception exception)
        {
            Diagnostics.DroppedFrameCount++;
            throw CreateRenderException("Skia render", target, exception);
        }

        if (clear)
            canvas.Clear();
        return canvas;
    }

    internal override void EndRender(SkiaTarget target)
    {
        var webTarget = (SkiaWebGlTarget)target;
        try
        {
            BrowserCanvasSource source;
            try
            {
                source = _host.EndRenderNow();
            }
            catch (Exception exception)
            {
                Diagnostics.DroppedFrameCount++;
                throw CreateRenderException("Skia render", target, exception);
            }

            var uploadStart = Stopwatch.GetTimestamp();
            try
            {
                webTarget.Texture.UploadFromCanvas(source, CreateUploadOptions());
            }
            catch (Exception exception)
            {
                Diagnostics.DroppedFrameCount++;
                throw CreateRenderException("canvas upload", target, exception);
            }
            finally
            {
                if (_options.EnableDiagnostics)
                    Diagnostics.LastUploadCpuMilliseconds = Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
            }

            Diagnostics.UploadCount++;
            Diagnostics.BytesUploaded += (long)webTarget.Texture.Width * webTarget.Texture.Height * 4;
        }
        finally
        {
            if (_options.EnableDiagnostics)
                Diagnostics.LastRenderCpuMilliseconds = Stopwatch.GetElapsedTime(_renderStart).TotalMilliseconds;
        }
    }

    private CanvasUploadOptions CreateUploadOptions()
    {
        var useTexImage = _options.UploadMode == WebGlUploadMode.DiagnosticTexImage2D;
        Diagnostics.UploadPath = useTexImage ? "texImage2D(canvas)" : "texSubImage2D(canvas)";

        return new CanvasUploadOptions
        {
            ValidateDimensions = true,
            FlipY = _options.FlipY,
            PremultiplyAlpha = _options.PremultiplyAlpha,
            ColorSpaceConversion = _options.DisableColorSpaceConversion
                ? WebGLCanvasColorSpaceConversion.None
                : WebGLCanvasColorSpaceConversion.BrowserDefault,
            UploadMode = useTexImage
                ? WebGLCanvasUploadMode.TexImage2D
                : WebGLCanvasUploadMode.TexSubImage2D,
        };
    }

    private InvalidOperationException CreateRenderException(string stage, SkiaTarget target, Exception inner) =>
        new($"WebGL {stage} failed for {target.Texture.Width}x{target.Texture.Height} RGBA8 " +
            $"using {Diagnostics.UploadPath}; contextLost={_host.IsContextLost}; " +
            $"browserContext='{_host.WebGlVersion}'; cause={inner.GetType().Name}: {inner.Message}", inner);

    private void EnsureThread()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SkiaWebGlBackend));
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("WebGL backend calls must run on the graphics browser thread.");
    }

    private void OnContextLost(object? sender, EventArgs args) => Diagnostics.ContextLossCount++;
    private void OnContextRestored(object? sender, EventArgs args)
    {
        Diagnostics.SurfaceRecreationCount++;
        _sourceWidth = 0;
        _sourceHeight = 0;
    }

    public override void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _host.ContextLost -= OnContextLost;
        _host.ContextRestored -= OnContextRestored;
    }

    private sealed class SkiaWebGlTarget : SkiaTarget
    {
        private Texture2D? _texture;

        internal SkiaWebGlTarget(Texture2D texture) => _texture = texture;
        public override Texture2D Texture => _texture
            ?? throw new ObjectDisposedException(nameof(SkiaWebGlTarget));
        internal override void DisposeSkiaResources() { }
        internal override void DisposeGraphicsResources()
        {
            _texture?.Dispose();
            _texture = null;
        }
    }
}
