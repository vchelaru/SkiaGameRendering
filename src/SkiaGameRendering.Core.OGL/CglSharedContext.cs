using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// The macOS <see cref="ISharedGlContext"/>: raw CGL calls, the equivalent of
    /// <see cref="WglSharedContext"/> and <see cref="GlxSharedContext"/>. GLFW's and SDL's
    /// <c>NSOpenGLContext</c> both sit on a CGL context, and making one current through
    /// <c>NSOpenGLContext</c> makes its CGL context current, so CGL alone can swap between them.
    /// <para>
    /// The Skia context is created from the host context's own pixel format, so it gets the same
    /// profile (raylib asks for a 3.3 core profile). It is never attached to a drawable: Skia only
    /// draws into FBOs, which need none.
    /// </para>
    /// </summary>
    public sealed class CglSharedContext : ISharedGlContext
    {
        private const string OpenGLFramework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";

        [DllImport(OpenGLFramework)]
        private static extern IntPtr CGLGetCurrentContext();

        [DllImport(OpenGLFramework)]
        private static extern int CGLSetCurrentContext(IntPtr ctx);

        [DllImport(OpenGLFramework)]
        private static extern IntPtr CGLGetPixelFormat(IntPtr ctx);

        [DllImport(OpenGLFramework)]
        private static extern int CGLCreateContext(IntPtr pix, IntPtr share, out IntPtr ctx);

        [DllImport(OpenGLFramework)]
        private static extern int CGLDestroyContext(IntPtr ctx);

        private IntPtr _openGl;
        private IntPtr _previousContext;

        public IntPtr SkiaContext { get; private set; }

        public void CreateSharedContext(IntPtr windowHandle)
        {
            var hostContext = CGLGetCurrentContext();
            if (hostContext == IntPtr.Zero)
                throw new InvalidOperationException("CGLGetCurrentContext returned null - no context current on this thread.");

            var pixelFormat = CGLGetPixelFormat(hostContext);
            if (pixelFormat == IntPtr.Zero)
                throw new InvalidOperationException("CGLGetPixelFormat returned null for the host context.");

            var error = CGLCreateContext(pixelFormat, hostContext, out var skiaContext);
            if (error != 0)
                throw new InvalidOperationException($"CGLCreateContext failed (CGLError {error}).");

            SkiaContext = skiaContext;
            _openGl = NativeLibrary.Load(OpenGLFramework);
        }

        public void MakeSkiaContextCurrent()
        {
            _previousContext = CGLGetCurrentContext();
            var error = CGLSetCurrentContext(SkiaContext);
            if (error != 0)
                throw new InvalidOperationException($"CGLSetCurrentContext(Skia) failed (CGLError {error}).");
        }

        public void RestoreHostContext()
        {
            var error = CGLSetCurrentContext(_previousContext);
            if (error != 0)
                throw new InvalidOperationException($"CGLSetCurrentContext(host) failed (CGLError {error}).");
        }

        /// <summary>
        /// macOS has no GetProcAddress for GL: every entry point the system supports is a plain export
        /// of the OpenGL framework, for every context.
        /// </summary>
        public IntPtr GetProcAddress(string name) =>
            NativeLibrary.TryGetExport(_openGl, name, out var address) ? address : IntPtr.Zero;

        /// <summary>Destroys the Skia context. It must not be current on any thread.</summary>
        public void Dispose()
        {
            if (SkiaContext != IntPtr.Zero)
            {
                CGLDestroyContext(SkiaContext);
                SkiaContext = IntPtr.Zero;
            }
            if (_openGl != IntPtr.Zero)
            {
                NativeLibrary.Free(_openGl);
                _openGl = IntPtr.Zero;
            }
        }
    }
}
