using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;
using System.Reflection;
using System.Runtime.InteropServices;
using static SDL3.SDL;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for FNA running on FNA3D's OpenGL driver. Windows, Linux and macOS.
    ///
    /// Same design as MonoGame DesktopGL's <c>SkiaGlBackend</c>: a second SDL GL context is created
    /// sharing FNA's, Skia gets its own context to leave in whatever state it likes, and the
    /// texture objects are visible from both. FNA3D caches GL state aggressively, which is exactly
    /// why Skia must not draw on FNA's context.
    ///
    /// REQUIRES FNA3D's OpenGL DRIVER. FNA3D picks its SDL_GPU driver by default on SDL3 builds,
    /// and that driver hands out no native context (see <see cref="Fna3dSysRenderer"/>). Set the
    /// <c>FNA3D_FORCE_DRIVER</c> environment variable to <c>OpenGL</c> before constructing the
    /// <c>Game</c>. <see cref="Initialize"/> throws with that instruction if any other driver is
    /// active. Only FNA's SDL3 platform is supported (the default); the SDL calls here go through
    /// the SDL3 binding FNA itself ships.
    ///
    /// MAINTENANCE NOTES:
    /// - <c>GraphicsDevice.GLDevice</c> and <c>Texture.texture</c> are FNA internals reached by
    ///   reflection, pinned by <c>tests/Tests.Fna.OGL</c>.
    /// - An <c>FNA3D_Texture*</c> from the OpenGL driver is an <c>OpenGLTexture</c> struct
    ///   (<c>src/FNA3D_Driver_OpenGL.c</c>, "Cast from FNA3D_Texture*") whose FIRST field is the
    ///   <c>uint32_t</c> GL texture name. FNA3D has no API for reading it back, so
    ///   <see cref="CaptureTextureHandle"/> reads that first field and checks it with
    ///   <c>glIsTexture</c> on the shared context, so a reordered struct is an exception rather
    ///   than a silent black texture. The vendored <c>FNA3D.dll</c> under <c>external/fnalibs</c>
    ///   is the build this was verified against.
    /// </summary>
    public class SkiaFnaGlBackend : SkiaBackend
    {
        delegate byte IsTextureDelegate(uint texture);

        IntPtr _window;
        IntPtr _fnaContext;
        IntPtr _skiaContext;
        GRContext _grContext = null!;
        GlFunctions _gl = null!;
        IsTextureDelegate _isTexture = null!;

        // Reflection handles for FNA internals
        static FieldInfo? _glDeviceField;
        static FieldInfo? _textureField;

        public override GRContext GRContext => _grContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            _glDeviceField ??= typeof(GraphicsDevice).GetField("GLDevice", flags);
            var fna3dDevice = (IntPtr?)_glDeviceField?.GetValue(graphicsDevice) ?? IntPtr.Zero;
            if (fna3dDevice == IntPtr.Zero)
                throw new Exception("Could not extract GLDevice (the FNA3D_Device*) from GraphicsDevice.");

            _textureField ??= typeof(Texture).GetField("texture", flags)
                ?? throw new Exception("Could not find the texture field on Texture.");

            var sysRenderer = Fna3dSysRenderer.Get(fna3dDevice);
            if (sysRenderer.RendererType != Fna3dSysRendererType.OpenGL)
                throw new InvalidOperationException(
                    $"FNA3D is running its {sysRenderer.RendererType} driver, and SkiaGameRendering.Fna.OGL needs " +
                    "its OpenGL driver. Set the FNA3D_FORCE_DRIVER environment variable to \"OpenGL\" before " +
                    "constructing the Game.");

            // FNA3D leaves its context current on the game thread between frames, so the current
            // window/context pair is FNA's. Cross-checking against what FNA3D reported catches a
            // mismatch in the SysRenderer struct layout before anything is built on top of it.
            _fnaContext = SDL_GL_GetCurrentContext();
            _window = SDL_GL_GetCurrentWindow();
            if (_fnaContext == IntPtr.Zero || _window == IntPtr.Zero)
                throw new InvalidOperationException("No SDL GL context is current on this thread; initialize from the game thread.");
            if (_fnaContext != sysRenderer.OpenGLContext)
                throw new InvalidOperationException(
                    "FNA3D_GetSysRendererEXT reported a different GL context than the one current on this thread.");

            // FNA3D already set the version/profile attributes it wanted before creating its own
            // context; SDL keeps them, so the context created here matches FNA's.
            if (!SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_SHARE_WITH_CURRENT_CONTEXT, 1))
                throw new Exception($"SDL_GL_SetAttribute failed: {SDL_GetError()}");

            _skiaContext = SDL_GL_CreateContext(_window);
            if (_skiaContext == IntPtr.Zero)
                throw new Exception($"SDL_GL_CreateContext failed: {SDL_GetError()}");

            MakeSkiaContextCurrent();
            try
            {
                var loader = new SdlGlFunctionLoader();
                _gl = GlFunctions.Load(loader);
                _isTexture = loader.Load<IsTextureDelegate>("glIsTexture");
                _grContext = GlGrContextFactory.Create(_gl);
            }
            finally
            {
                MakeEngineContextCurrent();
            }
        }

        void MakeSkiaContextCurrent()
        {
            if (!SDL_GL_MakeCurrent(_window, _skiaContext))
                throw new Exception($"SDL_GL_MakeCurrent (Skia) failed: {SDL_GetError()}");
        }

        void MakeEngineContextCurrent()
        {
            if (!SDL_GL_MakeCurrent(_window, _fnaContext))
                throw new Exception($"SDL_GL_MakeCurrent (FNA) failed: {SDL_GetError()}");
        }

        internal override void BeginDraw()
        {
            MakeSkiaContextCurrent();
            _grContext.ResetContext();
        }

        internal override void EndDraw() => MakeEngineContextCurrent();

        internal override object CaptureTextureHandle(Texture2D texture)
        {
            // FNA allocates the GL texture (glTexImage2D per level) in the Texture2D constructor.
            var fna3dTexture = (IntPtr?)_textureField!.GetValue(texture) ?? IntPtr.Zero;
            if (fna3dTexture == IntPtr.Zero)
                throw new Exception($"FNA3D texture is null on Texture2D ({texture.Width}x{texture.Height}).");

            // OpenGLTexture.handle - see the MAINTENANCE NOTES on this class.
            var glTextureId = (uint)Marshal.ReadInt32(fna3dTexture);

            // Called from CreateTarget, which brackets it with BeginDraw/EndDraw, so the Skia
            // context is current and shares FNA's texture namespace.
            if (glTextureId == 0 || _isTexture(glTextureId) == 0)
                throw new InvalidOperationException(
                    $"The first field of FNA3D's OpenGL texture struct ({glTextureId}) is not a GL texture name. " +
                    "The vendored FNA3D.dll's OpenGLTexture layout has changed; see SkiaFnaGlBackend's MAINTENANCE NOTES.");

            return new GlTextureState { TextureId = (int)glTextureId };
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

        internal override void BindForDrawing(object renderState) =>
            GlSkiaSurfaceFactory.BindForDrawing(_gl, (GlFramebufferState)renderState);

        internal override void UnbindAfterDrawing() => GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);

        internal override void DisposeRenderState(object renderState) =>
            GlSkiaSurfaceFactory.DisposeRenderState(_gl, (GlFramebufferState)renderState);

        public override void Dispose()
        {
            if (_skiaContext == IntPtr.Zero)
                return;

            // The GRContext belongs to the Skia context, so it has to be current while Skia frees
            // its GL objects; the context itself goes once that's done.
            MakeSkiaContextCurrent();
            try
            {
                _grContext?.Dispose();
            }
            finally
            {
                MakeEngineContextCurrent();
            }
            SDL_GL_DestroyContext(_skiaContext);
            _skiaContext = IntPtr.Zero;
        }

        internal sealed class GlTextureState
        {
            internal int TextureId;
        }

        /// <summary>
        /// Resolves GL entry points through SDL while the Skia context is current, so they are
        /// bound against the context they will be called on.
        /// </summary>
        sealed class SdlGlFunctionLoader : IGlFunctionLoader
        {
            public T Load<T>(string nativeName) where T : Delegate
            {
                var procAddress = SDL_GL_GetProcAddress(nativeName);
                if (procAddress == IntPtr.Zero)
                    throw new InvalidOperationException($"SDL_GL_GetProcAddress returned null for '{nativeName}'.");
                return Marshal.GetDelegateForFunctionPointer<T>(procAddress);
            }
        }
    }
}
