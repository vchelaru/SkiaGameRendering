using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using static SkiaGameRendering.GlWrapper;
using static SkiaGameRendering.SdlGlConstants;

namespace SkiaGameRendering
{
    internal class GlTextureState
    {
        internal int TextureId;
    }

    public class SkiaGlBackend : SkiaBackend
    {
        IntPtr _windowId;
        IntPtr _mgContextId;
        IntPtr _skContextId;
        GRContext _grContext;
        GlFunctions _gl;

        public override GRContext GRContext => _grContext;

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
            _grContext = GRContext.CreateGl();
            MakeEngineContextCurrent();
        }

        void MakeSkiaContextCurrent()
        {
            var result = MakeCurrent(_windowId, _skContextId);
            if (result < 0)
                throw new Exception("SDL_GL_MakeCurrent failed.");
        }

        void MakeEngineContextCurrent()
        {
            var result = MakeCurrent(_windowId, _mgContextId);
            if (result < 0)
                throw new Exception("SDL_GL_MakeCurrent failed.");
        }

        internal override void BeginDraw()
        {
            MakeSkiaContextCurrent();
            _grContext.ResetContext();
        }

        internal override void EndDraw()
        {
            MakeEngineContextCurrent();
        }

        internal override object CaptureTextureHandle(Texture2D texture)
        {
            MgGlFunctions.GetInteger(GL_TEXTURE_BINDING_2D, out var textureId);
            return new GlTextureState { TextureId = textureId };
        }

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var state = (GlTextureState)textureHandle;

            var result = GlSkiaSurfaceFactory.CreateSurface(
                _grContext, _gl, state.TextureId, width, height, colorType, out var framebufferState);

            renderState = framebufferState;
            return result;
        }

        internal override void BindForDrawing(object renderState)
        {
            GlSkiaSurfaceFactory.BindForDrawing(_gl, (GlFramebufferState)renderState);
        }

        internal override void UnbindAfterDrawing()
        {
            GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);
        }

        internal override void DisposeRenderState(object renderState)
        {
            GlSkiaSurfaceFactory.DisposeRenderState(_gl, (GlFramebufferState)renderState);
        }

        public override void Dispose()
        {
            _grContext?.Dispose();
        }
    }
}
