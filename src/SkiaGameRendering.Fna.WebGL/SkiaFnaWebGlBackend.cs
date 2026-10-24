using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for FNA in the browser: .NET WebAssembly with FNA3D's OpenGL driver on WebGL2.
    ///
    /// FNA3D and Skia drive ONE WebGL context (WebGL cannot share objects between contexts), the one
    /// Emscripten created for the page's canvas. Skia draws into an FNA texture's GL object directly, or
    /// into whatever framebuffer FNA has bound for <see cref="SkiaDirectCanvas"/>. FNA3D caches the GL state
    /// it has applied and has no API to invalidate that cache, so every Skia pass saves the whole GL state
    /// and restores it afterward (<see cref="FnaWebGlInterop"/>).
    ///
    /// REQUIRES the page's canvas to have the id given to the constructor ("canvas" by default), FNA on
    /// its SDL2 platform backend, and FNA3D's OpenGL driver (the only one that exists on WebAssembly).
    ///
    /// MAINTENANCE NOTES:
    /// - <c>Texture.texture</c> is an FNA internal reached by reflection, pinned by the Playwright tests.
    /// - An <c>FNA3D_Texture*</c> from the OpenGL driver is an <c>OpenGLTexture</c> struct whose FIRST
    ///   field is the <c>uint32_t</c> GL texture name, which is also the name Emscripten gives the
    ///   WebGLTexture. <see cref="CaptureTextureId"/> checks it against the texture FNA3D left bound, so a
    ///   reordered struct is an exception rather than a silent black texture.
    /// </summary>
    [SupportedOSPlatform("browser")]
    public sealed class SkiaFnaWebGlBackend : SkiaBackend
    {
        private const uint GlTexture2D = 0x0DE1;
        private const uint GlRgba8 = 0x8058;
        private static FieldInfo? _textureField;

        private readonly string _canvasId;
        private GRContext _grContext = null!;
        private bool _isDisposed;
        private bool _contextLost;
        private DirectTarget? _backBufferTarget;
        private readonly Dictionary<RenderTarget2D, DirectTarget> _renderTargetTargets = new();
        private readonly RenderTargetBinding[] _boundRenderTarget = new RenderTargetBinding[1];

        /// <param name="canvasId">The id of the page's canvas, the one SDL was created on.</param>
        public SkiaFnaWebGlBackend(string canvasId = "canvas") => _canvasId = canvasId;

        public override GRContext GRContext => _grContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            GraphicsDevice = graphicsDevice;
            FnaWebGlInterop.EnsureDefined();

            // FNA3D leaves its context current between frames, so Skia's interface binds to it.
            var info = FnaWebGlInterop.Begin(_canvasId);
            try
            {
                if (info[0] != 0)
                    throw ContextLostException();
                var glInterface = GRGlInterface.Create()
                    ?? throw new InvalidOperationException("GRGlInterface.Create failed; is the page's WebGL context current?");
                _grContext = GRContext.CreateGl(glInterface)
                    ?? throw new InvalidOperationException("GRContext.CreateGl failed on FNA's WebGL context.");
            }
            finally
            {
                FnaWebGlInterop.End(_canvasId);
            }
        }

        /// <summary>GL errors Skia left queued that were cleared so FNA3D would not see them.</summary>
        public int ClearedGlErrorCount => FnaWebGlInterop.ErrorCount();

        internal override void BeginDraw() => BeginPass();

        /// <summary>Saves FNA3D's GL state and tells Skia to forget its own. Returns the framebuffer info of FNA's current target.</summary>
        private int[] BeginPass()
        {
            EnsureAlive();
            if (_contextLost)
                throw ContextLostException();
            var info = FnaWebGlInterop.Begin(_canvasId);
            if (info[0] != 0)
            {
                // FNA3D's buffers, programs and textures died with the context and are not rebuilt, and neither
                // are Skia's. Stay failed even if the browser restores the context.
                _contextLost = true;
                _grContext.AbandonContext();
                throw ContextLostException();
            }
            _grContext.ResetContext();
            return info;
        }

        private static InvalidOperationException ContextLostException() => new(
            "The WebGL context was lost. FNA does not recover from a lost context, so SkiaGameRendering.Fna.WebGL " +
            "cannot draw again; dispose the game and start a new one.");

        internal override void EndDraw() => FnaWebGlInterop.End(_canvasId);

        internal override SkiaTarget CreateTarget(int width, int height, SKColorType colorType)
        {
            EnsureAlive();
            if (colorType != SKColorType.Rgba8888)
                throw new NotSupportedException(
                    $"SkiaRenderTarget2D supports SKColorType.Rgba8888 only on FNA WebGL, not {colorType}.");
            var texture = CreateTexture(width, height, ToSurfaceFormat(colorType));
            try
            {
                var textureId = CaptureTextureId(texture);
                // FNA3D leaves the texture it just created bound, which is how the first field is checked.
                var bound = FnaWebGlInterop.BoundTexture(_canvasId);
                if (textureId == 0 || bound != textureId)
                    throw new InvalidOperationException(
                        $"The first field of FNA3D's OpenGL texture struct ({textureId}) is not the GL texture FNA3D just created ({bound}). " +
                        "FNA3D's OpenGLTexture layout has changed; see SkiaFnaWebGlBackend's MAINTENANCE NOTES.");
                BeginDraw();
                try
                {
                    var backendTexture = new GRBackendTexture(width, height, false, new GRGlTextureInfo(GlTexture2D, (uint)textureId, GlRgba8));
                    // FNA's first texture row is the image's top, and it is GL row 0.
                    var surface = SKSurface.Create(_grContext, backendTexture, GRSurfaceOrigin.TopLeft, SKColorType.Rgba8888);
                    if (surface == null)
                    {
                        backendTexture.Dispose();
                        throw new InvalidOperationException($"SKSurface.Create failed for a {width}x{height} FNA texture.");
                    }
                    return new WebGlTarget(this, texture, surface, backendTexture);
                }
                finally
                {
                    EndDraw();
                }
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }

        internal override SKCanvas BeginRender(SkiaTarget target, bool clear)
        {
            var webTarget = target as WebGlTarget ?? throw new ArgumentException("The target was not created by this backend.", nameof(target));
            BeginDraw();
            if (clear)
                webTarget.Surface!.Canvas.Clear();
            return webTarget.Surface!.Canvas;
        }

        internal override void EndRender(SkiaTarget target)
        {
            var webTarget = (WebGlTarget)target;
            try
            {
                webTarget.Surface!.Flush();
                _grContext.Flush();
            }
            finally
            {
                EndDraw();
            }
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(Texture))]
        private static int CaptureTextureId(Texture2D texture)
        {
            _textureField ??= typeof(Texture).GetField("texture", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingMemberException("Texture.texture was not found; FNA's internal layout changed.");
            var fna3dTexture = (IntPtr?)_textureField.GetValue(texture) ?? IntPtr.Zero;
            if (fna3dTexture == IntPtr.Zero)
                throw new InvalidOperationException($"FNA3D texture is null on Texture2D ({texture.Width}x{texture.Height}).");

            // OpenGLTexture.handle, the first field.
            var handle = Marshal.ReadInt32(fna3dTexture);
            return handle;
        }

        private void EnsureAlive()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(SkiaFnaWebGlBackend));
        }

        // --- SkiaDirectCanvas ---

        internal override bool SupportsDirectCanvas => true;

        private sealed class DirectTarget
        {
            internal SKSurface Surface = null!;
            internal GRBackendRenderTarget RenderTarget = null!;
            internal int Framebuffer;
            internal int Width;
            internal int Height;
            internal int Samples;
            internal int StencilBits;
        }

        private sealed class DirectPass
        {
            internal DirectTarget Target = null!;
            internal int SaveCount;
        }

        internal override SKCanvas BeginDirectDraw(out object pass)
        {
            EnsureAlive();
            var renderTarget = GetBoundRenderTarget();
            if (renderTarget != null && renderTarget.Format != SurfaceFormat.Color)
                throw new NotSupportedException($"SkiaDirectCanvas supports SurfaceFormat.Color render targets, not {renderTarget.Format}.");

            var info = BeginPass();
            try
            {
                var framebuffer = info[1];
                var parameters = GraphicsDevice.PresentationParameters;
                var width = renderTarget?.Width ?? (framebuffer == 0 ? info[4] : parameters.BackBufferWidth);
                var height = renderTarget?.Height ?? (framebuffer == 0 ? info[5] : parameters.BackBufferHeight);
                var target = GetDirectTarget(renderTarget, framebuffer, width, height, info[2], info[3]);
                var canvas = target.Surface.Canvas;
                pass = new DirectPass { Target = target, SaveCount = canvas.Save() };
                return canvas;
            }
            catch
            {
                EndDraw();
                throw;
            }
        }

        internal override void EndDirectDraw(object pass)
        {
            var directPass = (DirectPass)pass;
            try
            {
                directPass.Target.Surface.Canvas.RestoreToCount(directPass.SaveCount);
                directPass.Target.Surface.Flush();
                _grContext.Flush();
            }
            finally
            {
                EndDraw();
            }
        }

        private RenderTarget2D? GetBoundRenderTarget()
        {
            var count = GraphicsDevice.GetRenderTargetsNoAllocEXT(null);
            if (count == 0)
                return null;
            if (count > 1)
                throw new NotSupportedException("SkiaDirectCanvas needs a single bound render target, not " + count + ".");

            GraphicsDevice.GetRenderTargetsNoAllocEXT(_boundRenderTarget);
            var renderTarget = _boundRenderTarget[0].RenderTarget as RenderTarget2D
                ?? throw new NotSupportedException("SkiaDirectCanvas supports a RenderTarget2D, not a cube target.");
            _boundRenderTarget[0] = default;
            return renderTarget;
        }

        private DirectTarget GetDirectTarget(RenderTarget2D? renderTarget, int framebuffer, int width, int height, int samples, int stencilBits)
        {
            PruneDisposedRenderTargets();
            DirectTarget? cached = null;
            if (renderTarget == null)
                cached = _backBufferTarget;
            else
                _renderTargetTargets.TryGetValue(renderTarget, out cached);

            if (cached != null && cached.Framebuffer == framebuffer && cached.Width == width && cached.Height == height
                && cached.Samples == samples && cached.StencilBits == stencilBits)
                return cached;

            if (cached != null)
                DisposeDirectTarget(cached);

            var glFramebuffer = new GRGlFramebufferInfo((uint)framebuffer, GlRgba8);
            var backendRenderTarget = new GRBackendRenderTarget(width, height, samples, stencilBits, glFramebuffer);
            // The canvas and FNA3D's own back buffer FBO are bottom-up. A render target's texture has the
            // image's first row at GL row 0, so Skia's top is there.
            var origin = renderTarget == null ? GRSurfaceOrigin.BottomLeft : GRSurfaceOrigin.TopLeft;
            var surface = SKSurface.Create(_grContext, backendRenderTarget, origin, SKColorType.Rgba8888);
            if (surface == null)
            {
                backendRenderTarget.Dispose();
                throw new InvalidOperationException($"SKSurface.Create failed for FNA's {width}x{height} framebuffer ({samples} samples, {stencilBits} stencil bits).");
            }

            var target = new DirectTarget
            {
                Surface = surface,
                RenderTarget = backendRenderTarget,
                Framebuffer = framebuffer,
                Width = width,
                Height = height,
                Samples = samples,
                StencilBits = stencilBits,
            };
            if (renderTarget == null)
                _backBufferTarget = target;
            else
                _renderTargetTargets[renderTarget] = target;
            return target;
        }

        private readonly List<RenderTarget2D> _disposedRenderTargets = new();

        // A disposed RenderTarget2D's framebuffer name can be handed to the next one FNA creates.
        private void PruneDisposedRenderTargets()
        {
            foreach (var renderTarget in _renderTargetTargets.Keys)
            {
                if (renderTarget.IsDisposed)
                    _disposedRenderTargets.Add(renderTarget);
            }
            foreach (var renderTarget in _disposedRenderTargets)
            {
                DisposeDirectTarget(_renderTargetTargets[renderTarget]);
                _renderTargetTargets.Remove(renderTarget);
            }
            _disposedRenderTargets.Clear();
        }

        private static void DisposeDirectTarget(DirectTarget target)
        {
            target.Surface.Dispose();
            target.RenderTarget.Dispose();
        }

        public override void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;

            if (_grContext == null)
                return;
            var deviceAlive = GraphicsDevice is { IsDisposed: false } && !_contextLost;
            if (deviceAlive)
                FnaWebGlInterop.Begin(_canvasId);
            try
            {
                if (_backBufferTarget != null)
                    DisposeDirectTarget(_backBufferTarget);
                _backBufferTarget = null;
                foreach (var target in _renderTargetTargets.Values)
                    DisposeDirectTarget(target);
                _renderTargetTargets.Clear();
                if (!deviceAlive)
                    _grContext.AbandonContext();
                _grContext.Dispose();
            }
            finally
            {
                if (deviceAlive)
                    FnaWebGlInterop.End(_canvasId);
            }
            _grContext = null!;
        }

        private sealed class WebGlTarget : SkiaTarget
        {
            private readonly SkiaFnaWebGlBackend _backend;
            private Texture2D? _texture;
            private GRBackendTexture? _backendTexture;

            internal WebGlTarget(SkiaFnaWebGlBackend backend, Texture2D texture, SKSurface surface, GRBackendTexture backendTexture)
            {
                _backend = backend;
                _texture = texture;
                Surface = surface;
                _backendTexture = backendTexture;
            }

            internal SKSurface? Surface { get; private set; }

            public override Texture2D Texture => _texture ?? throw new ObjectDisposedException(nameof(WebGlTarget));

            internal override bool ReadPixels(SKImageInfo dstInfo, IntPtr dstPixels, int dstRowBytes, int srcX, int srcY) =>
                (Surface ?? throw new ObjectDisposedException(nameof(WebGlTarget))).ReadPixels(dstInfo, dstPixels, dstRowBytes, srcX, srcY);

            internal override void DisposeSkiaResources()
            {
                if (Surface == null)
                    return;
                var deviceAlive = !_backend._isDisposed && !_backend._contextLost && _backend.GraphicsDevice is { IsDisposed: false } && _backend._grContext != null;
                if (deviceAlive)
                    FnaWebGlInterop.Begin(_backend._canvasId);
                try
                {
                    Surface.Dispose();
                    _backendTexture?.Dispose();
                }
                finally
                {
                    if (deviceAlive)
                        FnaWebGlInterop.End(_backend._canvasId);
                }
                Surface = null;
                _backendTexture = null;
            }

            internal override void DisposeGraphicsResources()
            {
                _texture?.Dispose();
                _texture = null;
            }
        }
    }
}
