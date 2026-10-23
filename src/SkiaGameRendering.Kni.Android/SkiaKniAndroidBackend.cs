using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering.Kni.Android
{
    /// <summary>
    /// SkiaBackend for KNI's Android (GLES) platform. Same design as the desktop GL backends: a second
    /// EGL context (<see cref="EglSharedContext"/>) shares KNI's, Skia gets it to leave in whatever
    /// state it likes, and the texture objects are visible from both. KNI caches GL state, which is
    /// why Skia must not draw on KNI's own context.
    /// <para>
    /// No reflection: KNI creates its context through Java's EGL10 but makes it current on the
    /// thread's native EGL state, which <see cref="EglSharedContext"/> reads with the native
    /// <c>eglGetCurrent*</c> calls, and <c>Texture2D.GetSharedHandle()</c> is public.
    /// </para>
    /// </summary>
    public class SkiaKniAndroidBackend : SkiaSharedGlBackend
    {
        readonly EglSharedContext _egl = new();

        /// <summary>The Skia context's GLES major version and whether it runs surfaceless; for diagnostics.</summary>
        public string Description => $"GLES {_egl.ClientVersion}, {(_egl.Surfaceless ? "surfaceless" : "host surface")}";

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            _egl.CreateSharedContext(graphicsDevice.PresentationParameters.DeviceWindowHandle);

            _egl.MakeSkiaContextCurrent();
            try
            {
                _gl = GlFunctions.Load(new SharedGlContextFunctionLoader(_egl));
                _grContext = GlGrContextFactory.Create(_gl);
            }
            finally
            {
                _egl.RestoreHostContext();
            }
        }

        private protected override void MakeSkiaContextCurrent() => _egl.MakeSkiaContextCurrent();

        private protected override void MakeEngineContextCurrent() => _egl.RestoreHostContext();

        // A plain Texture2D has no shared-handle support in KNI - only a RenderTarget2D constructed
        // with shared:true exposes GetSharedHandle(). DepthFormat.None/0 samples because Skia
        // manages its own depth/stencil renderbuffer in GlSkiaSurfaceFactory.
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents, shared: true);
        }

        private protected override int CaptureGlTextureId(Texture2D texture) => (int)texture.GetSharedHandle();

        private readonly KniBoundRenderTarget _boundRenderTarget = new();

        private protected override bool CanDrawDirect => true;

        private protected override RenderTarget2D? GetBoundRenderTarget() => _boundRenderTarget.Get(GraphicsDevice);

        // Skia's context normally runs surfaceless, which has no default framebuffer, so drawing to the
        // back buffer needs it current on KNI's window surface.
        private protected override void MakeSkiaContextCurrentOnEngineWindow() => _egl.MakeSkiaContextCurrentOnHostSurface();

        public override void Dispose()
        {
            if (_grContext != null)
                DisposeDirectTargets();
            _grContext?.Dispose();
            _egl.Dispose();
        }
    }
}
