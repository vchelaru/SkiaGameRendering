using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using static SkiaGameRendering.GlWrapper;
using static SkiaGameRendering.SdlGlConstants;

namespace SkiaGameRendering
{
    public class SkiaGlBackend : SkiaSharedGlBackend
    {
        IntPtr _windowId;
        IntPtr _mgContextId;
        IntPtr _skContextId;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            _windowId = GetMgWindowId(graphicsDevice);
            _mgContextId = SDL_GL_GetCurrentContext();

            MgGlFunctions.LoadFunctions();

            var setAttributeResult = SDL_GL_SetAttribute(SDL_GL_SHARE_WITH_CURRENT_CONTEXT, 1);
            if (setAttributeResult < 0)
                throw new Exception("SDL_GL_SetAttribute failed.");

            _skContextId = SDL_GL_CreateContext(_windowId);
            if (_skContextId == IntPtr.Zero)
                throw new Exception("SDL_GL_CreateContext failed.");

            MakeSkiaContextCurrent();
            _gl = GlFunctions.Load(new MonoGameGlFunctionLoader());
            _grContext = GlGrContextFactory.Create(_gl);
            MakeEngineContextCurrent();
        }

        private protected override void MakeSkiaContextCurrent()
        {
            var result = MakeCurrent(_windowId, _skContextId);
            if (result < 0)
                throw new Exception("SDL_GL_MakeCurrent failed.");
        }

        private protected override void MakeEngineContextCurrent()
        {
            var result = MakeCurrent(_windowId, _mgContextId);
            if (result < 0)
                throw new Exception("SDL_GL_MakeCurrent failed.");
        }

        private protected override int CaptureGlTextureId(Texture2D texture)
        {
            MgGlFunctions.GetInteger(GL_TEXTURE_BINDING_2D, out var textureId);
            return textureId;
        }

        private readonly MonoGameBoundRenderTarget _boundRenderTarget = new();

        private protected override bool CanDrawDirect => true;

        private protected override RenderTarget2D? GetBoundRenderTarget() => _boundRenderTarget.Get(GraphicsDevice);

        private protected override void DestroySkiaContext()
        {
            if (_skContextId == IntPtr.Zero)
                return;
            DeleteContext(_skContextId);
            _skContextId = IntPtr.Zero;
        }
    }
}
