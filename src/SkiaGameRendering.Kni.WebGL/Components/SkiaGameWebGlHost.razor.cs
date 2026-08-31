using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SkiaGameRendering.Kni.WebGL;
using SkiaSharp;

namespace SkiaGameRendering.Kni.WebGL.Components;

[SupportedOSPlatform("browser")]
public partial class SkiaGameWebGlHost : ComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/SkiaGameRendering.Kni.WebGL/skia-game-webgl.js";
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _canvasElementId = $"skia-game-source-{Guid.NewGuid():N}";
    private IJSInProcessObjectReference? _module;
    private DotNetObjectReference<SkiaGameWebGlHost>? _selfReference;
    // A SkiaRenderTarget2D's Skia-side surface is bound to this host's single physical <canvas>
    // element/framebuffer (KNI's texSubImage2D(canvas) upload reads the whole canvas, so its size
    // must exactly match whatever's being uploaded - see repo issue #12 item 6). Consumers commonly
    // alternate between a small, stable set of distinct target sizes every frame (e.g. a main canvas
    // plus a UI panel); caching by size instead of disposing on every mismatch turns that into a
    // handful of one-time allocations instead of full GRBackendRenderTarget/SKSurface recreation on
    // every single draw.
    private const int MaxCachedSurfaces = 4;
    private readonly List<CachedSurface> _surfaceCache = new(); // LRU order: index 0 = least recent
    private HostGlInfo? _glInfo;
    private GRGlInterface? _glInterface;
    private GRContext? _context;
    private SKSurface? _surface;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private int _ownerThreadId;
    private bool _isRendering;
    private int _activeSaveCount;
    private bool _isDisposed;
    private bool _isContextLost;

    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Parameter] public bool RequireWebGl2 { get; set; } = true;
    [Parameter] public int ResourceCacheBytes { get; set; } = 256 * 1024 * 1024;

    public string CanvasElementId => _canvasElementId;
    public Task Ready => _ready.Task;
    public bool IsReady => _ready.Task.IsCompletedSuccessfully && !_isDisposed;
    public bool IsContextLost => _isContextLost;
    public double DevicePixelRatio => _glInfo?.DevicePixelRatio ?? 1;
    public string WebGlVersion => _glInfo?.Version ?? string.Empty;
    public string Renderer => _glInfo?.Renderer ?? string.Empty;
    public GRContext GRContext => _context
        ?? throw new InvalidOperationException("The Skia WebGL context has not rendered yet.");

    public event EventHandler? ContextLost;
    public event EventHandler? ContextRestored;

    internal void ConfigureResourceCache(int resourceCacheBytes)
    {
        if (resourceCacheBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(resourceCacheBytes));
        ResourceCacheBytes = resourceCacheBytes;
        _context?.SetResourceCacheLimit(resourceCacheBytes);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            var imported = await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _module = imported as IJSInProcessObjectReference
                ?? throw new PlatformNotSupportedException("Synchronous WebGL interop requires Blazor WebAssembly.");
            _selfReference = DotNetObjectReference.Create(this);
            InterceptBrowserObjects();
            _glInfo = _module.Invoke<HostGlInfo>("initialize", CanvasElementId, _selfReference, new
            {
                requireWebGl2 = RequireWebGl2,
                premultipliedAlpha = true,
            });
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _ready.TrySetResult();
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            throw;
        }
    }

    public BrowserCanvasSource Resize(int physicalWidth, int physicalHeight)
    {
        EnsureUsable();
        ValidateSize(physicalWidth, physicalHeight);
        _module!.InvokeVoid("makeCurrent", CanvasElementId, physicalWidth, physicalHeight);
        return new BrowserCanvasSource(CanvasElementId, physicalWidth, physicalHeight);
    }

    public BrowserCanvasSource RenderNow(int physicalWidth, int physicalHeight, Action<SKSurface> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        EnsureUsable();
        if (_isRendering)
            throw new InvalidOperationException("Skia WebGL rendering is not reentrant.");

        _isRendering = true;
        try
        {
            var source = PrepareFrame(physicalWidth, physicalHeight);
            var canvas = _surface!.Canvas;
            var saveCount = canvas.Save();
            try
            {
                draw(_surface);
            }
            finally
            {
                canvas.RestoreToCount(saveCount);
            }

            FlushFrame();
            return source;
        }
        finally
        {
            _isRendering = false;
        }
    }

    /// <summary>
    /// Begins a render pass: prepares the frame, ensures the surface exists, and returns its
    /// canvas. Pair with <see cref="EndRenderNow"/>. Used by <c>SkiaWebGlBackend</c> so a
    /// <see cref="SkiaGameRendering.SkiaRenderTarget2D"/>'s Begin/End can hold the canvas open
    /// across arbitrary caller code, instead of requiring a single atomic draw callback.
    /// </summary>
    internal SKCanvas BeginRenderNow(int physicalWidth, int physicalHeight)
    {
        EnsureUsable();
        if (_isRendering)
            throw new InvalidOperationException("Skia WebGL rendering is not reentrant.");

        _isRendering = true;
        PrepareFrame(physicalWidth, physicalHeight);
        _activeSaveCount = _surface!.Canvas.Save();
        return _surface.Canvas;
    }

    /// <summary>
    /// Ends the render pass started by <see cref="BeginRenderNow"/>: flushes Skia's queued GPU
    /// work and returns the browser canvas source to upload from.
    /// </summary>
    internal BrowserCanvasSource EndRenderNow()
    {
        try
        {
            _surface!.Canvas.RestoreToCount(_activeSaveCount);
            FlushFrame();
            return new BrowserCanvasSource(CanvasElementId, _surfaceWidth, _surfaceHeight);
        }
        finally
        {
            _isRendering = false;
        }
    }

    private BrowserCanvasSource PrepareFrame(int physicalWidth, int physicalHeight)
    {
        var source = Resize(physicalWidth, physicalHeight);
        EnsureSurface(physicalWidth, physicalHeight);
        return source;
    }

    private void FlushFrame()
    {
        _surface!.Flush();
        _context!.Flush();
    }

    [JSInvokable]
    public void OnWebGlContextLost()
    {
        _isContextLost = true;
        DisposeGpuResources();
        ContextLost?.Invoke(this, EventArgs.Empty);
    }

    [JSInvokable]
    public void OnWebGlContextRestored()
    {
        _isContextLost = false;
        ContextRestored?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureSurface(int width, int height)
    {
        if (_context == null)
        {
            _glInterface = GRGlInterface.Create()
                ?? throw new InvalidOperationException("GRGlInterface.Create failed for the browser context.");
            _context = GRContext.CreateGl(_glInterface)
                ?? throw new InvalidOperationException("GRContext.CreateGl failed for the browser context.");
            _context.SetResourceCacheLimit(ResourceCacheBytes);
        }

        var index = _surfaceCache.FindIndex(cached => cached.Width == width && cached.Height == height);
        CachedSurface entry;
        if (index >= 0)
        {
            entry = _surfaceCache[index];
            _surfaceCache.RemoveAt(index);
        }
        else
        {
            if (_surfaceCache.Count >= MaxCachedSurfaces)
            {
                _surfaceCache[0].Dispose();
                _surfaceCache.RemoveAt(0);
            }

            var framebufferInfo = new GRGlFramebufferInfo(_glInfo!.FboId, SKColorType.Rgba8888.ToGlSizedFormat());
            var renderTarget = new GRBackendRenderTarget(width, height, _glInfo.Samples, _glInfo.Stencils, framebufferInfo);
            var surface = SKSurface.Create(_context, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
                ?? throw new InvalidOperationException("SKSurface.Create failed for the browser framebuffer.");
            entry = new CachedSurface(width, height, renderTarget, surface);
        }

        _surfaceCache.Add(entry); // move to the most-recently-used position
        _surface = entry.Surface;
        _surfaceWidth = width;
        _surfaceHeight = height;
    }

    private void EnsureUsable()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(SkiaGameWebGlHost));
        if (!IsReady)
            throw new InvalidOperationException("Await SkiaGameWebGlHost.Ready before rendering.");
        if (_isContextLost)
            throw new InvalidOperationException("The Skia WebGL context is lost.");
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Skia WebGL calls must run on the host browser thread.");
    }

    private static void ValidateSize(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
    }

    private void DisposeSurface()
    {
        foreach (var cached in _surfaceCache)
            cached.Dispose();
        _surfaceCache.Clear();
        _surface = null;
    }

    private void DisposeGpuResources()
    {
        DisposeSurface();
        _context?.Dispose();
        _context = null;
        _glInterface?.Dispose();
        _glInterface = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        DisposeGpuResources();

        if (_module != null)
        {
            _module.InvokeVoid("dispose", CanvasElementId);
            await _module.DisposeAsync();
        }

        _selfReference?.Dispose();
        _selfReference = null;
        _module = null;
    }

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    private static extern void InterceptBrowserObjects();

    private sealed class HostGlInfo
    {
        public int ContextId { get; set; }
        public uint FboId { get; set; }
        public int Stencils { get; set; }
        public int Samples { get; set; }
        public int Depth { get; set; }
        public double DevicePixelRatio { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Renderer { get; set; } = string.Empty;
    }

    private sealed class CachedSurface(int width, int height, GRBackendRenderTarget renderTarget, SKSurface surface) : IDisposable
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public SKSurface Surface { get; } = surface;

        public void Dispose()
        {
            Surface.Dispose();
            renderTarget.Dispose();
        }
    }
}
