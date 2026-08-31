using System.Runtime.InteropServices;

namespace SkiaGameRendering.Raylib
{
    /// <summary>
    /// Raw WGL calls to create a second GL context that shares raylib's object namespace (textures,
    /// buffers, programs) but has entirely independent bound state.
    /// <para>
    /// rlgl (raylib's GL layer) and Skia both issue raw GL calls against whatever context is
    /// current. A spike (see issue #3) found that interleaving them on a single shared context
    /// corrupts rlgl's own state - its default-font text drew as solid color blocks instead of
    /// glyphs, and Skia's own texture didn't display at all - even after resyncing the specific
    /// state fields that looked suspect (active texture unit, rlgl's texture-bind cache). A
    /// glReadPixels readback confirmed Skia's draw itself was correct throughout; the corruption
    /// happened on rlgl's side after it regained control, not in Skia's rendering.
    /// </para>
    /// <para>
    /// The fix is a second GL context that shares raylib's object namespace - the same trick
    /// <c>SkiaGlBackend</c> already plays for MonoGame DesktopGL via
    /// <c>SDL_GL_CreateContext(SHARE_WITH_CURRENT_CONTEXT)</c>. Two contexts on the same window/HDC
    /// never fight over what's currently bound (shader program, VAO, texture units, blend mode) -
    /// only GL *objects* are shared - so nothing needs manual save/restore around the Skia draw.
    /// </para>
    /// <para>
    /// raylib statically links GLFW and doesn't export its context-creation entry points, so this
    /// goes straight to Win32/WGL instead of GLFW. Windows-only implementation of
    /// <see cref="IPlatformGlContext"/> - see <see cref="Glx"/> for the Linux/X11 equivalent (GLX
    /// instead of WGL). macOS is not implemented (see issue #10).
    /// </para>
    /// </summary>
    internal sealed class Wgl : IPlatformGlContext
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetCurrentContext();

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglCreateContext(IntPtr hdc);

        [DllImport("opengl32.dll")]
        private static extern bool wglShareLists(IntPtr hglrc1, IntPtr hglrc2);

        [DllImport("opengl32.dll")]
        private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

        [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr wglGetProcAddress(string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        public IntPtr Hdc { get; private set; }
        public IntPtr EngineContext { get; private set; }
        public IntPtr SkiaContext { get; private set; }

        public void CreateSharedContext(IntPtr windowHandle)
        {
            Hdc = GetDC(windowHandle);
            if (Hdc == IntPtr.Zero)
                throw new InvalidOperationException("GetDC failed.");

            EngineContext = wglGetCurrentContext();
            if (EngineContext == IntPtr.Zero)
                throw new InvalidOperationException("wglGetCurrentContext returned null - no context current on this thread.");

            SkiaContext = wglCreateContext(Hdc);
            if (SkiaContext == IntPtr.Zero)
                throw new InvalidOperationException("wglCreateContext failed.");

            if (!wglShareLists(EngineContext, SkiaContext))
                throw new InvalidOperationException("wglShareLists failed.");
        }

        public void MakeSkiaContextCurrent()
        {
            if (!wglMakeCurrent(Hdc, SkiaContext))
                throw new InvalidOperationException("wglMakeCurrent(Skia) failed.");
        }

        public void MakeEngineContextCurrent()
        {
            if (!wglMakeCurrent(Hdc, EngineContext))
                throw new InvalidOperationException("wglMakeCurrent(engine) failed.");
        }

        /// <summary>
        /// wglGetProcAddress only resolves functions beyond GL 1.1 (returns null, or on some drivers
        /// a bogus 1/2/3 sentinel, for anything in the base profile like glGetIntegerv). Fall back to
        /// a direct GetProcAddress against opengl32.dll for those.
        /// </summary>
        public IntPtr GetProcAddress(string name)
        {
            var address = wglGetProcAddress(name);
            if (address != IntPtr.Zero && address.ToInt64() is not (1 or 2 or 3 or -1))
                return address;

            var openGl32 = GetModuleHandle("opengl32.dll");
            return GetProcAddress(openGl32, name);
        }
    }
}
