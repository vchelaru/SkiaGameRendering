using Godot;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering.Godot
{
    /// <summary>
    /// The backend for Godot's Compatibility renderer (<c>rendering_method = gl_compatibility</c>,
    /// driver <c>opengl3</c>), which has no <see cref="RenderingDevice"/>: Godot renders with a native
    /// OpenGL 3.3 context of its own. The shape is the raylib adapter's, not the RD backends': a
    /// second GL context that shares Godot's object namespace (textures, buffers, programs) but has
    /// independent bound state, so Skia's GL calls never disturb the GLES3 renderer's heavily cached
    /// state and vice versa. Skia draws through an FBO it wraps around the GL texture behind an
    /// ordinary <see cref="ImageTexture"/> - zero-copy - and the scene tree samples that texture.
    ///
    /// MAINTENANCE NOTES:
    /// <list type="bullet">
    /// <item>
    /// <b>Godot hands out everything needed through public API</b> (4.3+):
    /// <see cref="DisplayServer.WindowGetNativeHandle"/> gives the window (<c>HWND</c> on Windows, the
    /// X11 <c>Window</c> on Linux/X11) and <see cref="RenderingServer.TextureGetNativeHandle"/> gives
    /// an <see cref="ImageTexture"/>'s <c>GLuint</c>. Godot's own context is current on the render
    /// thread, which is where this runs, so Core.OGL's <see cref="WglSharedContext"/>/<see cref="GlxSharedContext"/>
    /// (shared with the raylib adapter) read it with <c>wglGetCurrentContext</c>/<c>glXGetCurrentContext</c>
    /// and create the sharing context from it. After each Skia draw they restore whichever context
    /// and window were current before it, so a multi-window project keeps drawing to the right one.
    /// </item>
    /// <item>
    /// <b>Windows native WGL and Linux X11/GLX only.</b> Godot's other GL flavors - <c>opengl3_angle</c>
    /// (ANGLE over D3D11 on Windows/macOS), <c>opengl3_es</c> (Android), Wayland and macOS native -
    /// use EGL or NSOpenGL contexts this adapter has no platform code for yet;
    /// <see cref="Initialize"/> reports which. Web exports cannot P/Invoke GL at all.
    /// </item>
    /// <item>
    /// <b>Texture orientation.</b> Godot uploads image row 0 (the top) to GL texel row 0 and its
    /// canvas samples <c>v = 0</c> as the top, so Skia must write canvas row 0 into texel row 0:
    /// <see cref="GRSurfaceOrigin.TopLeft"/> (Core.OGL's default, as the MonoGame backend uses). NOT
    /// the raylib adapter's <c>BottomLeft</c>, which puts canvas row 0 in the last texel row for hosts
    /// that sample the other way up and shows every texture upside down here. A symmetric test image
    /// hides the difference.
    /// </item>
    /// <item>
    /// <b>Synchronization</b> is GL's shared-object rule: the writing context flushes (Skia's
    /// <c>SKSurface.Flush</c> ends in <c>glFlush</c>) and the reading context binds the texture, which
    /// Godot's canvas renderer does per draw. Same contract the MonoGame DesktopGL and raylib adapters
    /// rely on. No layouts, no hand-back barrier, no priming, and the surface persists across frames.
    /// </item>
    /// <item>
    /// <b>RGBA8 only.</b> Godot's <see cref="Image.Format"/> has no BGRA or 10-bit variants that map onto
    /// Skia's other GPU color types, so the constructor rejects everything but <see cref="SKColorType.Rgba8888"/>.
    /// </item>
    /// </list>
    /// </summary>
    internal sealed class GlCompatibilityGodotBackend : SkiaGodotBackend
    {
        ISharedGlContext? _platform;
        GRContext? _grContext;
        GlFunctions? _gl;

        internal override string DriverName => "opengl3";

        internal override bool IsZeroCopy => true;

        internal override void Initialize(RenderingDevice? explicitDevice)
        {
            var displayServer = DisplayServer.GetName();
            ISharedGlContext platform;
            if (OperatingSystem.IsWindows() && displayServer == "Windows")
                platform = new WglSharedContext();
            else if (OperatingSystem.IsLinux() && displayServer == "X11")
                platform = new GlxSharedContext();
            else
                throw new PlatformNotSupportedException(
                    $"SkiaGameRendering.Godot supports Godot's Compatibility renderer on Windows (native WGL) and Linux X11 (GLX) only; " +
                    $"this is '{displayServer}' on {OS.GetName()}. Use the Forward+ or Mobile renderer here, or run X11 instead of Wayland.");

            var windowHandle = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
            if (windowHandle == IntPtr.Zero)
                throw new InvalidOperationException("DisplayServer.WindowGetNativeHandle(WindowHandle) returned null.");

            // Owned from here on, so Dispose releases whatever a failed CreateSharedContext made.
            _platform = platform;
            platform.CreateSharedContext(windowHandle);

            platform.MakeSkiaContextCurrent();
            try
            {
                _gl = GlFunctions.Load(new SharedGlContextFunctionLoader(platform));
                _grContext = GlGrContextFactory.Create(_gl);
            }
            finally
            {
                platform.RestoreHostContext();
            }
        }

        protected override SkiaGodotTargetResources CreateTargetCore(int width, int height, SKColorType colorType)
        {
            if (colorType != SKColorType.Rgba8888)
                throw new NotSupportedException(
                    $"Godot's Compatibility renderer backend supports SKColorType.Rgba8888 only (Godot's Image formats have no {colorType} equivalent).");
            return new TargetResources(this, width, height);
        }

        /// <summary>Nothing is in flight across frames on this backend: EndFrame flushes the GL stream.</summary>
        internal override void WaitForPendingGpuWork() { }

        void BeginDraw()
        {
            _platform!.MakeSkiaContextCurrent();
            _grContext!.ResetContext();
        }

        void EndDraw() => _platform!.RestoreHostContext();

        public override void Dispose()
        {
            if (_grContext != null)
            {
                BeginDraw();
                try
                {
                    _grContext.Dispose();
                    _grContext = null;
                }
                finally
                {
                    EndDraw();
                }
            }
            _platform?.Dispose();
            _platform = null;
        }

        sealed class TargetResources : SkiaGodotTargetResources
        {
            readonly GlCompatibilityGodotBackend _backend;
            ImageTexture? _texture;
            SKSurface? _surface;
            GRBackendRenderTarget? _renderTarget;
            GlFramebufferState? _framebuffer;
            bool _disposed;

            internal TargetResources(GlCompatibilityGodotBackend backend, int width, int height)
            {
                _backend = backend;

                // An ordinary Godot texture; the GLES3 renderer allocates its GL texture (RGBA8) right
                // here, and TextureGetNativeHandle is the public way to get its name.
                using var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
                _texture = ImageTexture.CreateFromImage(image)
                    ?? throw new InvalidOperationException("ImageTexture.CreateFromImage returned null.");
                var glTexture = (int)RenderingServer.TextureGetNativeHandle(_texture.GetRid(), false);
                if (glTexture == 0)
                    throw new InvalidOperationException("RenderingServer.TextureGetNativeHandle returned 0 for the Skia texture.");

                backend.BeginDraw();
                try
                {
                    (_surface, _renderTarget) = GlSkiaSurfaceFactory.CreateSurface(
                        backend._grContext!, backend._gl!, glTexture, width, height, SKColorType.Rgba8888,
                        out var framebuffer, GRSurfaceOrigin.TopLeft);
                    _framebuffer = framebuffer;
                    if (_surface == null)
                        throw new InvalidOperationException("SKSurface.Create failed for the GL framebuffer wrapping Godot's texture.");
                }
                catch
                {
                    if (_framebuffer != null)
                        GlSkiaSurfaceFactory.DisposeRenderState(backend._gl!, _framebuffer);
                    _renderTarget?.Dispose();
                    backend.EndDraw();
                    throw;
                }
                backend.EndDraw();
            }

            internal override Texture2D Texture => _texture ?? throw new ObjectDisposedException(nameof(SkiaGodotRenderTarget2D));

            internal override Rid TextureRid => _disposed ? throw new ObjectDisposedException(nameof(SkiaGodotRenderTarget2D)) : default;

            internal override SKSurface BeginFrame()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _backend.BeginDraw();
                GlSkiaSurfaceFactory.BindForDrawing(_backend._gl!, _framebuffer!);
                return _surface!;
            }

            internal override void EndFrame()
            {
                try
                {
                    _surface!.Canvas.RestoreToCount(1);
                    _surface.Flush();
                }
                finally
                {
                    GlSkiaSurfaceFactory.UnbindAfterDrawing(_backend._gl!);
                    _backend.EndDraw();
                }
            }

            /// <summary>
            /// Nothing to detach on this backend: the scene tree keeps sampling the ImageTexture, which
            /// Godot frees when the last reference goes. The FBO must go before that (see <see cref="Dispose"/>).
            /// </summary>
            internal override void ReleaseSceneTexture() { }

            public override void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _backend.Untrack(this);

                _backend.BeginDraw();
                try
                {
                    _surface?.Dispose();
                    _surface = null;
                    _renderTarget?.Dispose();
                    _renderTarget = null;
                    if (_framebuffer != null)
                        GlSkiaSurfaceFactory.DisposeRenderState(_backend._gl!, _framebuffer);
                    _framebuffer = null;
                }
                finally
                {
                    _backend.EndDraw();
                }
                // Refcounted; a node still showing it keeps it (and its last contents) alive.
                _texture = null;
            }
        }
    }
}
