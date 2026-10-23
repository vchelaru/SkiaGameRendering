using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// The Android (and any native-EGL) <see cref="ISharedGlContext"/>: raw EGL calls, the equivalent of
    /// <see cref="GlxSharedContext"/>. A host that creates its GL ES context through Java's EGL10 (KNI
    /// on Android) still makes it current on the thread's native EGL state, so the native
    /// <c>eglGetCurrent*</c> calls see it.
    /// <para>
    /// The Skia context is created against the host's own EGLConfig (read back with
    /// <c>eglQueryContext(EGL_CONFIG_ID)</c>) and its GLES major version, sharing the host's objects.
    /// Skia only draws into FBOs, so it needs no surface of its own: it goes current surfaceless
    /// (<c>EGL_KHR_surfaceless_context</c>) where the driver allows that, and on the host's current
    /// draw surface otherwise. <see cref="Surfaceless"/> says which one a device took.
    /// </para>
    /// </summary>
    public sealed class EglSharedContext : ISharedGlContext
    {
        private const string LibEgl = "libEGL.so";
        private const string LibGles = "libGLESv2.so";

        private const int EGL_NONE = 0x3038;
        private const int EGL_CONFIG_ID = 0x3028;
        private const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;
        private const int EGL_OPENGL_ES_API = 0x30A0;

        private static readonly IntPtr EGL_NO_SURFACE = IntPtr.Zero;
        private static readonly IntPtr EGL_NO_CONTEXT = IntPtr.Zero;

        [DllImport(LibEgl)]
        private static extern IntPtr eglGetCurrentContext();

        [DllImport(LibEgl)]
        private static extern IntPtr eglGetCurrentDisplay();

        // readdraw: EGL_DRAW = 0x3059, EGL_READ = 0x305A
        [DllImport(LibEgl)]
        private static extern IntPtr eglGetCurrentSurface(int readdraw);

        [DllImport(LibEgl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool eglQueryContext(IntPtr dpy, IntPtr ctx, int attribute, out int value);

        [DllImport(LibEgl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool eglChooseConfig(IntPtr dpy, int[] attribList, IntPtr[] configs, int configSize, out int numConfig);

        [DllImport(LibEgl)]
        private static extern IntPtr eglCreateContext(IntPtr dpy, IntPtr config, IntPtr shareContext, int[] attribList);

        [DllImport(LibEgl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool eglDestroyContext(IntPtr dpy, IntPtr ctx);

        [DllImport(LibEgl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool eglMakeCurrent(IntPtr dpy, IntPtr draw, IntPtr read, IntPtr ctx);

        [DllImport(LibEgl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool eglBindAPI(int api);

        [DllImport(LibEgl)]
        private static extern int eglGetError();

        [DllImport(LibEgl, CharSet = CharSet.Ansi)]
        private static extern IntPtr eglGetProcAddress(string procName);

        private IntPtr _previousDisplay;
        private IntPtr _previousDrawSurface;
        private IntPtr _previousReadSurface;
        private IntPtr _previousContext;
        private IntPtr _glesLibrary;
        private SurfaceMode _surfaceMode;

        private enum SurfaceMode { Unknown, Surfaceless, Fallback }

        public IntPtr Display { get; private set; }
        public IntPtr SkiaContext { get; private set; }

        /// <summary>The host's current draw surface, used only when the driver rejects a surfaceless context.</summary>
        public IntPtr FallbackSurface { get; private set; }

        /// <summary>True when the Skia context runs with no surface (<c>EGL_NO_SURFACE</c>).</summary>
        public bool Surfaceless => _surfaceMode == SurfaceMode.Surfaceless;

        /// <summary>The host context's GLES major version (2 or 3).</summary>
        public int ClientVersion { get; private set; }

        /// <summary><paramref name="windowHandle"/> is unused: EGL carries the display and surfaces on the host's current context.</summary>
        public void CreateSharedContext(IntPtr windowHandle)
        {
            var hostContext = eglGetCurrentContext();
            if (hostContext == EGL_NO_CONTEXT)
                throw new InvalidOperationException("eglGetCurrentContext returned EGL_NO_CONTEXT - no context current on this thread.");

            Display = eglGetCurrentDisplay();
            if (Display == IntPtr.Zero)
                throw new InvalidOperationException("eglGetCurrentDisplay returned EGL_NO_DISPLAY.");

            FallbackSurface = eglGetCurrentSurface(0x3059);

            if (!eglQueryContext(Display, hostContext, EGL_CONFIG_ID, out var configId))
                throw new InvalidOperationException($"eglQueryContext(EGL_CONFIG_ID) failed (EGL error 0x{eglGetError():X}).");
            if (!eglQueryContext(Display, hostContext, EGL_CONTEXT_CLIENT_VERSION, out var clientVersion))
                throw new InvalidOperationException($"eglQueryContext(EGL_CONTEXT_CLIENT_VERSION) failed (EGL error 0x{eglGetError():X}).");
            ClientVersion = clientVersion;

            var configs = new IntPtr[1];
            if (!eglChooseConfig(Display, new[] { EGL_CONFIG_ID, configId, EGL_NONE }, configs, 1, out var configCount) || configCount == 0)
                throw new InvalidOperationException($"eglChooseConfig found no EGLConfig matching id 0x{configId:X}.");

            // A context is created for the API bound on the calling thread, which defaults to GLES.
            eglBindAPI(EGL_OPENGL_ES_API);
            SkiaContext = eglCreateContext(Display, configs[0], hostContext,
                new[] { EGL_CONTEXT_CLIENT_VERSION, clientVersion, EGL_NONE });
            if (SkiaContext == EGL_NO_CONTEXT)
                throw new InvalidOperationException($"eglCreateContext failed (EGL error 0x{eglGetError():X}).");
        }

        public void MakeSkiaContextCurrent()
        {
            _previousDisplay = eglGetCurrentDisplay();
            _previousDrawSurface = eglGetCurrentSurface(0x3059);
            _previousReadSurface = eglGetCurrentSurface(0x305A);
            _previousContext = eglGetCurrentContext();

            // The first call finds out whether the driver takes a surfaceless context; later calls reuse that.
            if (_surfaceMode != SurfaceMode.Fallback &&
                eglMakeCurrent(Display, EGL_NO_SURFACE, EGL_NO_SURFACE, SkiaContext))
            {
                _surfaceMode = SurfaceMode.Surfaceless;
                return;
            }

            _surfaceMode = SurfaceMode.Fallback;
            if (!eglMakeCurrent(Display, FallbackSurface, FallbackSurface, SkiaContext))
                throw new InvalidOperationException($"eglMakeCurrent(Skia) failed (EGL error 0x{eglGetError():X}).");
        }

        /// <summary>
        /// Makes the Skia context current on the host's current draw and read surfaces, so its default
        /// framebuffer is the host's window. A surfaceless context has no default framebuffer at all. Read
        /// each time because the host recreates its window surface, on Android when the app resumes.
        /// </summary>
        public void MakeSkiaContextCurrentOnHostSurface()
        {
            _previousDisplay = eglGetCurrentDisplay();
            _previousDrawSurface = eglGetCurrentSurface(0x3059);
            _previousReadSurface = eglGetCurrentSurface(0x305A);
            _previousContext = eglGetCurrentContext();

            if (_previousDrawSurface == EGL_NO_SURFACE)
                throw new InvalidOperationException("The host context has no current draw surface.");
            if (!eglMakeCurrent(Display, _previousDrawSurface, _previousReadSurface, SkiaContext))
                throw new InvalidOperationException($"eglMakeCurrent(Skia, host surface) failed (EGL error 0x{eglGetError():X}).");
        }

        public void RestoreHostContext()
        {
            var display = _previousDisplay != IntPtr.Zero ? _previousDisplay : Display;
            if (!eglMakeCurrent(display, _previousDrawSurface, _previousReadSurface, _previousContext))
                throw new InvalidOperationException($"eglMakeCurrent(host) failed (EGL error 0x{eglGetError():X}).");
        }

        /// <summary>
        /// eglGetProcAddress resolves core GLES functions on Android's loader, but the spec only
        /// promises extensions (before EGL 1.5), so a miss falls back to the GLES library's exports.
        /// </summary>
        public IntPtr GetProcAddress(string name)
        {
            var address = eglGetProcAddress(name);
            if (address != IntPtr.Zero)
                return address;

            if (_glesLibrary == IntPtr.Zero)
                NativeLibrary.TryLoad(LibGles, out _glesLibrary);
            return _glesLibrary != IntPtr.Zero && NativeLibrary.TryGetExport(_glesLibrary, name, out var export)
                ? export
                : IntPtr.Zero;
        }

        /// <summary>Destroys the Skia context. It must not be current on any thread.</summary>
        public void Dispose()
        {
            if (SkiaContext != EGL_NO_CONTEXT)
            {
                eglDestroyContext(Display, SkiaContext);
                SkiaContext = EGL_NO_CONTEXT;
            }
        }
    }
}
