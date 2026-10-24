using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for the iOS (OpenGL ES) platforms of MonoGame and KNI. Same design as the Android backend, with an
    /// EAGL sharegroup in place of a shared EGL context (<see cref="EaglSharedContext"/>). MonoGame caches
    /// GL state, which is why Skia must not draw on MonoGame's context.
    /// <para>
    /// No reflection: MonoGame's context is <c>EAGLContext.CurrentContext</c> on the game thread, and a new
    /// <see cref="Texture2D"/> is left bound on it, so its GL name is <c>GL_TEXTURE_BINDING_2D</c>. MonoGame
    /// iOS has no default framebuffer: its back buffer is an FBO around a renderbuffer backed by the view's
    /// <c>CAEAGLLayer</c>, which it deletes and recreates on every view layout.
    /// </para>
    /// </summary>
    public partial class SkiaIosBackend : SkiaSharedGlBackend
    {
        readonly EaglSharedContext _eagl = new();

        /// <summary>The Skia context's EAGL API, e.g. <c>OpenGLES3</c>; for diagnostics.</summary>
        public string Description => _eagl.Api.ToString();

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            _eagl.CreateSharedContext(IntPtr.Zero);

            _eagl.MakeSkiaContextCurrent();
            try
            {
                _gl = GlFunctions.Load(new SharedGlContextFunctionLoader(_eagl));
                _grContext = GlGrContextFactory.Create(_gl);
            }
            finally
            {
                _eagl.RestoreHostContext();
            }
        }

        private protected override void MakeSkiaContextCurrent() => _eagl.MakeSkiaContextCurrent();

        private protected override void MakeEngineContextCurrent() => _eagl.RestoreHostContext();

        private protected override bool CanDrawDirect => true;

        private protected override bool BackBufferCanBeFramebufferObject => true;

        private protected override bool BackBufferRenderbufferCanBeReplaced => true;

        private protected override void DestroySkiaContext() => _eagl.Dispose();
    }
}
