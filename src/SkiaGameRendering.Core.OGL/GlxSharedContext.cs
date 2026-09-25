using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// The Linux/X11 <see cref="ISharedGlContext"/>: raw GLX calls, the equivalent of
    /// <see cref="WglSharedContext"/>. Native GLX rather than EGL because that is what GLFW's default
    /// X11 context API and Godot's X11 GL manager both create.
    /// <para>
    /// GLX has no per-drawable pixel format the way WGL has per-HDC, so creating a context needs an
    /// explicit <c>GLXFBConfig</c>. This reads the FBConfig ID off the host's current context with
    /// <c>glXQueryContext</c> and resolves the matching config with <c>glXChooseFBConfig</c>, so the
    /// Skia context uses exactly the host's framebuffer configuration.
    /// </para>
    /// </summary>
    public sealed class GlxSharedContext : ISharedGlContext
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
        private static extern void glXDestroyContext(IntPtr dpy, IntPtr ctx);

        [DllImport(LibGL)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool glXMakeCurrent(IntPtr dpy, IntPtr drawable, IntPtr ctx);

        [DllImport(LibGL, CharSet = CharSet.Ansi)]
        private static extern IntPtr glXGetProcAddress(string procName);

        [DllImport(LibX11)]
        private static extern int XDefaultScreen(IntPtr display);

        [DllImport(LibX11)]
        private static extern int XFree(IntPtr data);

        private IntPtr _previousDisplay;
        private IntPtr _previousDrawable;
        private IntPtr _previousContext;

        public IntPtr Display { get; private set; }
        public IntPtr Drawable { get; private set; }
        public IntPtr SkiaContext { get; private set; }

        public void CreateSharedContext(IntPtr windowHandle)
        {
            // windowHandle (the plain X11 Window) is deliberately not used as the drawable. GLFW's GLX
            // backend makes its context current on a separate GLXWindow from glXCreateWindow, and
            // passing the X11 Window to glXMakeCurrent fails with BadDrawable. The host's current
            // drawable is always the one it actually bound.
            var hostContext = glXGetCurrentContext();
            if (hostContext == IntPtr.Zero)
                throw new InvalidOperationException("glXGetCurrentContext returned null - no context current on this thread.");

            Drawable = glXGetCurrentDrawable();
            if (Drawable == IntPtr.Zero)
                throw new InvalidOperationException("glXGetCurrentDrawable returned null.");

            // The host already opened the X connection; its current context carries it.
            Display = glXGetCurrentDisplay();
            if (Display == IntPtr.Zero)
                throw new InvalidOperationException("glXGetCurrentDisplay returned null.");

            if (glXQueryContext(Display, hostContext, GLX_FBCONFIG_ID, out var fbConfigId) != 0)
                throw new InvalidOperationException("glXQueryContext(GLX_FBCONFIG_ID) failed.");

            var screen = XDefaultScreen(Display);
            var attribs = new[] { GLX_FBCONFIG_ID, fbConfigId, None };
            var configs = glXChooseFBConfig(Display, screen, attribs, out var configCount);
            if (configs == IntPtr.Zero || configCount == 0)
                throw new InvalidOperationException($"glXChooseFBConfig found no FBConfig matching id 0x{fbConfigId:X}.");

            var fbConfig = Marshal.ReadIntPtr(configs);
            XFree(configs);

            SkiaContext = glXCreateNewContext(Display, fbConfig, GLX_RGBA_TYPE, hostContext, true);
            if (SkiaContext == IntPtr.Zero)
                throw new InvalidOperationException("glXCreateNewContext failed.");
        }

        /// <summary>
        /// Skia only draws into FBOs, never a window's default framebuffer, so its context always goes
        /// current on the drawable it was created against, whichever host window is current.
        /// </summary>
        public void MakeSkiaContextCurrent()
        {
            _previousDisplay = glXGetCurrentDisplay();
            _previousDrawable = glXGetCurrentDrawable();
            _previousContext = glXGetCurrentContext();
            if (!glXMakeCurrent(Display, Drawable, SkiaContext))
                throw new InvalidOperationException("glXMakeCurrent(Skia) failed.");
        }

        public void RestoreHostContext()
        {
            // glXMakeCurrent needs a display even to release; fall back to ours when nothing was current.
            var display = _previousDisplay != IntPtr.Zero ? _previousDisplay : Display;
            if (!glXMakeCurrent(display, _previousDrawable, _previousContext))
                throw new InvalidOperationException("glXMakeCurrent(host) failed.");
        }

        /// <summary>
        /// Unlike wglGetProcAddress, glXGetProcAddress resolves the full GL API (including GL 1.1
        /// base-profile functions) with no fallback needed.
        /// </summary>
        public IntPtr GetProcAddress(string name) => glXGetProcAddress(name);

        /// <summary>Destroys the Skia context. It must not be current on any thread.</summary>
        public void Dispose()
        {
            if (SkiaContext != IntPtr.Zero)
            {
                glXDestroyContext(Display, SkiaContext);
                SkiaContext = IntPtr.Zero;
            }
        }
    }
}
