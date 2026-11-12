using System.Runtime.InteropServices;
using SkiaGameRendering.Core.OGL;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Skia's connection to the app's OpenGL context. Create one while the app's GL context is current, with the
    /// window library's proc-address function (BRUTAL's GLFW: <c>glfwGetProcAddress</c>; SDL: <c>SDL_GL_GetProcAddress</c>):
    /// <code>
    /// var context = new SkiaGlContext(name => Glfw.GetProcAddress(name));
    /// </code>
    /// Skia draws on that same context, so every create, dispose, <see cref="SkiaRenderTarget2D.Begin"/> and
    /// <see cref="SkiaRenderTarget2D.End"/> must run on the thread that owns it with that context current. The
    /// context must be OpenGL 3.3 or newer (core or compatibility).
    /// </summary>
    public sealed class SkiaGlContext : IDisposable
    {
        GRContext? _grContext;
        readonly GlFunctions _gl;
        readonly GlTextureFunctions _textures;

        public SkiaGlContext(Func<string, IntPtr> getProcAddress)
        {
            ArgumentNullException.ThrowIfNull(getProcAddress);
            var loader = new ProcAddressLoader(getProcAddress);
            _gl = GlFunctions.Load(loader);
            _textures = GlTextureFunctions.Load(loader);
            _grContext = GlGrContextFactory.Create(_gl);
            _getProcAddress = getProcAddress;
        }

        readonly Func<string, IntPtr> _getProcAddress;

        /// <summary>A holder for the app's GL state, which a pass saves when it begins and restores when it ends.</summary>
        internal GlStateSnapshot CreateStateSnapshot() => new(_getProcAddress);

        internal GRContext GrContext => _grContext ?? throw new ObjectDisposedException(nameof(SkiaGlContext));
        internal GlFunctions Gl => _gl;
        internal GlTextureFunctions Textures => _textures;

        /// <summary>Dispose every <see cref="SkiaRenderTarget2D"/> first. Call it with the app's GL context current.</summary>
        public void Dispose()
        {
            var grContext = _grContext;
            if (grContext == null)
                return;
            _grContext = null;
            grContext.Dispose();
        }

        sealed class ProcAddressLoader : IGlFunctionLoader
        {
            readonly Func<string, IntPtr> _getProcAddress;
            public ProcAddressLoader(Func<string, IntPtr> getProcAddress) => _getProcAddress = getProcAddress;

            public T Load<T>(string nativeName) where T : Delegate
            {
                var address = _getProcAddress(nativeName);
                if (address == IntPtr.Zero)
                    throw new EntryPointNotFoundException($"The GL proc-address function returned null for '{nativeName}'.");
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }
        }
    }
}
