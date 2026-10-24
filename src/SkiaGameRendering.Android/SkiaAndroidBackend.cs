using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for MonoGame's Android (GLES) platform. Same design as the KNI Android backend: a
    /// second EGL context (<see cref="EglSharedContext"/>) shares MonoGame's, and the texture objects are
    /// visible from both. MonoGame caches GL state, which is why Skia must not draw on MonoGame's context.
    /// <para>
    /// No reflection: MonoGame creates its context through Java's EGL10 but makes it current on the
    /// thread's native EGL state, which <see cref="EglSharedContext"/> reads with the native
    /// <c>eglGetCurrent*</c> calls. A new <see cref="Texture2D"/> is left bound on MonoGame's context,
    /// so its GL name is <c>GL_TEXTURE_BINDING_2D</c>, as on DesktopGL.
    /// </para>
    /// </summary>
    public class SkiaAndroidBackend : SkiaSharedGlBackend
    {
        private const int GL_TEXTURE_BINDING_2D = 0x8069;

        [DllImport("libGLESv2.so")]
        private static extern void glGetIntegerv(int pname, out int data);

        readonly EglSharedContext _egl = new();
        readonly MonoGameBoundRenderTarget _boundRenderTarget = new();

        /// <summary>The Skia context's GLES major version and whether it runs surfaceless; for diagnostics.</summary>
        public string Description => $"GLES {_egl.ClientVersion}, {(_egl.Surfaceless ? "surfaceless" : "host surface")}";

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            _egl.CreateSharedContext(IntPtr.Zero);

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

        // Called on MonoGame's context right after the texture was created.
        private protected override int CaptureGlTextureId(Texture2D texture)
        {
            glGetIntegerv(GL_TEXTURE_BINDING_2D, out var textureId);
            return textureId;
        }

        private protected override bool CanDrawDirect => true;

        private protected override RenderTarget2D? GetBoundRenderTarget() => _boundRenderTarget.Get(GraphicsDevice);

        // Skia's context normally runs surfaceless, which has no default framebuffer, so drawing to the
        // back buffer needs it current on MonoGame's window surface.
        private protected override void MakeSkiaContextCurrentOnEngineWindow() => _egl.MakeSkiaContextCurrentOnHostSurface();

        private protected override void DestroySkiaContext() => _egl.Dispose();
    }
}
