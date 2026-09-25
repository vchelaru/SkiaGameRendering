using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// The Windows <see cref="ISharedGlContext"/>: raw WGL calls that create a second context sharing
    /// the host's GL objects. Goes straight to WGL because hosts that statically link their windowing
    /// library (raylib's GLFW, Godot's own platform layer) do not export its context-creation entry
    /// points. The same trick <c>SkiaGlBackend</c> plays for MonoGame DesktopGL through
    /// <c>SDL_GL_CreateContext</c>.
    /// </summary>
    public sealed class WglSharedContext : ISharedGlContext
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetCurrentContext();

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglGetCurrentDC();

        [DllImport("opengl32.dll")]
        private static extern IntPtr wglCreateContext(IntPtr hdc);

        [DllImport("opengl32.dll")]
        private static extern bool wglDeleteContext(IntPtr hglrc);

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

        private IntPtr _window;
        private IntPtr _previousDc;
        private IntPtr _previousContext;

        public IntPtr Hdc { get; private set; }
        public IntPtr SkiaContext { get; private set; }

        public void CreateSharedContext(IntPtr windowHandle)
        {
            _window = windowHandle;
            Hdc = GetDC(windowHandle);
            if (Hdc == IntPtr.Zero)
                throw new InvalidOperationException("GetDC failed.");

            var hostContext = wglGetCurrentContext();
            if (hostContext == IntPtr.Zero)
                throw new InvalidOperationException("wglGetCurrentContext returned null - no context current on this thread.");

            SkiaContext = wglCreateContext(Hdc);
            if (SkiaContext == IntPtr.Zero)
                throw new InvalidOperationException("wglCreateContext failed.");

            if (!wglShareLists(hostContext, SkiaContext))
                throw new InvalidOperationException("wglShareLists failed.");
        }

        /// <summary>
        /// Skia only draws into FBOs, never a window's default framebuffer, so its context always goes
        /// current on the DC it was created on, whichever host window is current.
        /// </summary>
        public void MakeSkiaContextCurrent()
        {
            _previousDc = wglGetCurrentDC();
            _previousContext = wglGetCurrentContext();
            if (!wglMakeCurrent(Hdc, SkiaContext))
                throw new InvalidOperationException("wglMakeCurrent(Skia) failed.");
        }

        public void RestoreHostContext()
        {
            if (!wglMakeCurrent(_previousDc, _previousContext))
                throw new InvalidOperationException("wglMakeCurrent(host) failed.");
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

        /// <summary>Deletes the Skia context. It must not be current on any thread.</summary>
        public void Dispose()
        {
            if (SkiaContext != IntPtr.Zero)
            {
                wglDeleteContext(SkiaContext);
                SkiaContext = IntPtr.Zero;
            }
            if (Hdc != IntPtr.Zero)
            {
                ReleaseDC(_window, Hdc);
                Hdc = IntPtr.Zero;
            }
        }
    }
}
