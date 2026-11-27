using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for MonoGame's Android (GLES) platform. Same design as the KNI Android backend: a
    /// second EGL context (<see cref="EglSharedContext"/>) shares MonoGame's, and the texture objects are
    /// visible from both. MonoGame caches GL state, so Skia draws on MonoGame's context only for a back
    /// buffer <see cref="SkiaDirectCanvas"/> pass, after which MonoGame's state cache is invalidated.
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

        // Skia's context runs surfaceless and has no default framebuffer, and making it current on MonoGame's
        // window surface too loses finished writes on the Android emulator (two contexts current on one
        // window surface). So a back buffer pass draws on MonoGame's own context, which is then told to
        // apply all of its cached GL state again. Render target passes stay on Skia's context, surfaceless.
        private protected override bool DrawsBackBufferOnEngineContext => true;

        readonly GlesEngineStateReset _glesReset = new();
        readonly MonoGameStateInvalidation _invalidation = new();

        private protected override void RestoreEngineStateAfterSkia()
        {
            _glesReset.Reset();
            _invalidation.Invalidate(GraphicsDevice);
        }

        private protected override void DestroySkiaContext() => _egl.Dispose();
    }
}
