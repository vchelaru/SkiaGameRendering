using System.Runtime.InteropServices;

namespace SkiaGameRendering.Raylib
{
    /// <summary>
    /// Raw GLX (X11) calls to create a second GL context that shares raylib's object namespace, the
    /// Linux/X11 equivalent of <see cref="Wgl"/>. See <see cref="Wgl"/>'s doc comment (and issue #3)
    /// for why a second context is needed at all: rlgl and Skia both issue raw GL calls, and
    /// interleaving them on a single shared context corrupts rlgl's own rendering.
    /// <para>
    /// raylib's GLFW build on Linux targets X11 (confirmed by inspecting the bundled
    /// <c>libraylib.so</c>: it exports <c>glfwGetX11Window</c>/<c>glfwGetX11Display</c> and GLX
    /// entry points, no Wayland symbols), and GLFW's default context-creation API on X11 is native
    /// GLX rather than EGL, so this goes through GLX rather than EGL - matching what raylib/GLFW
    /// itself actually uses at runtime, not just "a Linux GL API that works".
    /// </para>
    /// <para>
    /// Unlike WGL, where <c>wglCreateContext(hdc)</c> implicitly picks up whatever pixel format was
    /// already set on that HDC, GLX has no such per-drawable implicit state - context creation needs
    /// an explicit <c>GLXFBConfig</c>. Rather than re-deriving one from scratch (and risking a
    /// mismatch with whatever raylib/GLFW actually chose), this reads the FBConfig ID directly off
    /// raylib's own current context via <c>glXQueryContext</c> and re-resolves the matching
    /// <c>GLXFBConfig</c> with <c>glXChooseFBConfig</c>, guaranteeing the new context is created
    /// against the exact same framebuffer configuration as the window.
    /// </para>
    /// </summary>
    internal sealed class Glx : IPlatformGlContext
    {
        private const string LibGL = "libGL.so.1";
        private const string LibX11 = "libX11.so.6";

        private const int GLX_FBCONFIG_ID = 0x8013;
        private const int GLX_RGBA_TYPE = 0x8014;
        private const int None = 0;

        [DllImport(LibGL)]
        private static extern IntPtr glXGetCurrentContext();

        [DllImport(LibGL)]
        private static extern IntPtr glXGetCurrentDisplay();

        [DllImport(LibGL)]
        private static extern IntPtr glXGetCurrentDrawable();

        [DllImport(LibGL)]
        private static extern int glXQueryContext(IntPtr dpy, IntPtr ctx, int attribute, out int value);

        [DllImport(LibGL)]
        private static extern IntPtr glXChooseFBConfig(IntPtr dpy, int screen, int[] attribList, out int nElements);

        [DllImport(LibGL)]
        private static extern IntPtr glXCreateNewContext(IntPtr dpy, IntPtr config, int renderType, IntPtr shareList, [MarshalAs(UnmanagedType.I1)] bool direct);

        [DllImport(LibGL)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool glXMakeCurrent(IntPtr dpy, IntPtr drawable, IntPtr ctx);

        [DllImport(LibGL, CharSet = CharSet.Ansi)]
        private static extern IntPtr glXGetProcAddress(string procName);

        [DllImport(LibX11)]
        private static extern int XDefaultScreen(IntPtr display);

        [DllImport(LibX11)]
        private static extern int XFree(IntPtr data);

        public IntPtr Display { get; private set; }
        public IntPtr Drawable { get; private set; }
        public IntPtr EngineContext { get; private set; }
        public IntPtr SkiaContext { get; private set; }

        public void CreateSharedContext(IntPtr windowHandle)
        {
            // windowHandle (Raylib.GetWindowHandle(), i.e. glfwGetX11Window()) is deliberately not
            // used as the drawable here. GLFW 3.4's GLX backend creates its own GLXWindow via
            // glXCreateWindow for the context it makes current, which is a distinct GLX drawable ID
            // from the plain X11 Window - passing the X11 Window straight to glXMakeCurrent produced
            // a BadDrawable X error (X_GLXGetDrawableAttributes) when verified under WSLg. Reading
            // the drawable off raylib's own current context via glXGetCurrentDrawable() instead
            // guarantees an exact match with whatever GLFW actually bound, whatever it is.
            EngineContext = glXGetCurrentContext();
            if (EngineContext == IntPtr.Zero)
                throw new InvalidOperationException("glXGetCurrentContext returned null - no context current on this thread.");

            Drawable = glXGetCurrentDrawable();
            if (Drawable == IntPtr.Zero)
                throw new InvalidOperationException("glXGetCurrentDrawable returned null.");

            // No XOpenDisplay call needed: raylib/GLFW already opened the connection, and the
            // context it made current on this thread carries it.
            Display = glXGetCurrentDisplay();
            if (Display == IntPtr.Zero)
                throw new InvalidOperationException("glXGetCurrentDisplay returned null.");

            if (glXQueryContext(Display, EngineContext, GLX_FBCONFIG_ID, out var fbConfigId) != 0)
                throw new InvalidOperationException("glXQueryContext(GLX_FBCONFIG_ID) failed.");

            var screen = XDefaultScreen(Display);
            var attribs = new[] { GLX_FBCONFIG_ID, fbConfigId, None };
            var configs = glXChooseFBConfig(Display, screen, attribs, out var configCount);
            if (configs == IntPtr.Zero || configCount == 0)
                throw new InvalidOperationException($"glXChooseFBConfig found no FBConfig matching id 0x{fbConfigId:X}.");

            var fbConfig = Marshal.ReadIntPtr(configs);
            XFree(configs);

            SkiaContext = glXCreateNewContext(Display, fbConfig, GLX_RGBA_TYPE, EngineContext, true);
            if (SkiaContext == IntPtr.Zero)
                throw new InvalidOperationException("glXCreateNewContext failed.");
        }

        public void MakeSkiaContextCurrent()
        {
            if (!glXMakeCurrent(Display, Drawable, SkiaContext))
                throw new InvalidOperationException("glXMakeCurrent(Skia) failed.");
        }

        public void MakeEngineContextCurrent()
        {
            if (!glXMakeCurrent(Display, Drawable, EngineContext))
                throw new InvalidOperationException("glXMakeCurrent(engine) failed.");
        }

        /// <summary>
        /// Unlike wglGetProcAddress, glXGetProcAddress resolves the full GL API (including GL 1.1
        /// base-profile functions) with no fallback needed.
        /// </summary>
        public IntPtr GetProcAddress(string name) => glXGetProcAddress(name);
    }
}
