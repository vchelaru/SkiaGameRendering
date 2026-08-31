using System.Reflection;
using System.Runtime.InteropServices;

namespace SkiaGameRendering
{
    internal static class AngleEgl
    {
        private const string LibEGL = "libEGL";
        private const string LibGLESv2 = "libGLESv2";

        static AngleEgl()
        {
            NativeLibrary.SetDllImportResolver(typeof(AngleEgl).Assembly, (name, assembly, searchPath) =>
            {
                if (name != LibEGL && name != LibGLESv2)
                    return IntPtr.Zero;

                var dllName = name + ".dll";

                // 1. Try app-local (bundled ANGLE DLLs next to the executable)
                var assemblyDir = Path.GetDirectoryName(assembly.Location);
                var localPath = Path.Combine(assemblyDir, dllName);
                if (NativeLibrary.TryLoad(localPath, out var handle))
                    return handle;

                // 2. Try runtimes folder (NuGet native assets)
                var arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "win-x64",
                    Architecture.X86 => "win-x86",
                    Architecture.Arm64 => "win-arm64",
                    _ => "win-x64"
                };
                var runtimesPath = Path.Combine(assemblyDir, "runtimes", arch, "native", dllName);
                if (NativeLibrary.TryLoad(runtimesPath, out handle))
                    return handle;

                // 3. Fall back to Edge WebView's ANGLE (present on most Windows 10/11 machines)
                var edgePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "Microsoft-Edge-WebView", dllName);
                if (NativeLibrary.TryLoad(edgePath, out handle))
                    return handle;

                return IntPtr.Zero;
            });
        }

        // EGL constants
        internal const int EGL_NONE = 0x3038;
        internal const int EGL_TRUE = 1;
        internal const int EGL_FALSE = 0;
        internal const int EGL_SUCCESS = 0x3000;
        internal const int EGL_DEFAULT_DISPLAY = 0;

        internal static readonly IntPtr EGL_NO_CONTEXT = IntPtr.Zero;
        internal static readonly IntPtr EGL_NO_DISPLAY = IntPtr.Zero;
        internal static readonly IntPtr EGL_NO_SURFACE = IntPtr.Zero;

        // EGL config attributes
        internal const int EGL_RED_SIZE = 0x3024;
        internal const int EGL_GREEN_SIZE = 0x3025;
        internal const int EGL_BLUE_SIZE = 0x3026;
        internal const int EGL_ALPHA_SIZE = 0x3027;
        internal const int EGL_DEPTH_SIZE = 0x3025;
        internal const int EGL_STENCIL_SIZE = 0x3026;
        internal const int EGL_SURFACE_TYPE = 0x3033;
        internal const int EGL_RENDERABLE_TYPE = 0x3040;
        internal const int EGL_PBUFFER_BIT = 0x0001;
        internal const int EGL_OPENGL_ES2_BIT = 0x0004;
        internal const int EGL_OPENGL_ES3_BIT = 0x0040;

        // EGL context attributes
        internal const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;

        // ANGLE platform
        internal const int EGL_PLATFORM_ANGLE_ANGLE = 0x3202;
        internal const int EGL_PLATFORM_ANGLE_TYPE_ANGLE = 0x3203;
        internal const int EGL_PLATFORM_ANGLE_TYPE_D3D11_ANGLE = 0x3208;
        internal const int EGL_PLATFORM_DEVICE_EXT = 0x313F;

        // ANGLE device
        internal const int EGL_D3D11_DEVICE_ANGLE = 0x33A1;

        // ANGLE texture import
        internal const int EGL_D3D_TEXTURE_ANGLE = 0x33A3;

        // Pbuffer attributes
        internal const int EGL_WIDTH = 0x3057;
        internal const int EGL_HEIGHT = 0x3058;
        internal const int EGL_TEXTURE_TARGET = 0x3081;
        internal const int EGL_TEXTURE_2D = 0x305F;
        internal const int EGL_TEXTURE_FORMAT = 0x3080;
        internal const int EGL_TEXTURE_RGBA = 0x305E;
        internal const int EGL_FLEXIBLE_SURFACE_COMPATIBILITY_SUPPORTED_ANGLE = 0x33A6;

        // EGL core functions
        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        internal static extern int eglGetError();

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        internal static extern IntPtr eglGetPlatformDisplayEXT(int platform, IntPtr nativeDisplay, int[] attribs);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool eglInitialize(IntPtr display, out int major, out int minor);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool eglChooseConfig(IntPtr display, int[] attribs, out IntPtr config, int configSize, out int numConfigs);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        internal static extern IntPtr eglCreateContext(IntPtr display, IntPtr config, IntPtr shareContext, int[] attribs);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool eglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool eglDestroyContext(IntPtr display, IntPtr context);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool eglDestroySurface(IntPtr display, IntPtr surface);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool eglTerminate(IntPtr display);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        internal static extern IntPtr eglCreatePbufferFromClientBuffer(
            IntPtr display, int buftype, IntPtr buffer, IntPtr config, int[] attribs);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        internal static extern IntPtr eglGetProcAddress(string procname);

        // ANGLE device extension
        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        internal static extern IntPtr eglCreateDeviceANGLE(int deviceType, IntPtr nativeDevice, int[] attribs);

        [DllImport(LibEGL, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool eglReleaseDeviceANGLE(IntPtr device);

        // GL ES functions (loaded from ANGLE's libGLESv2)
        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glGenFramebuffers(int n, out int framebuffers);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glBindFramebuffer(int target, int framebuffer);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glDeleteFramebuffers(int n, ref int framebuffers);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glFramebufferTexture2D(int target, int attachment, int textarget, int texture, int level);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glGenRenderbuffers(int n, out int renderbuffers);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glBindRenderbuffer(int target, int renderbuffer);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glRenderbufferStorage(int target, int internalformat, int width, int height);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glDeleteRenderbuffers(int n, ref int renderbuffers);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glFramebufferRenderbuffer(int target, int attachment, int renderbuffertarget, int renderbuffer);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern int glCheckFramebufferStatus(int target);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern unsafe void glGetIntegerv(int pname, int* data);

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glFlush();

        [DllImport(LibGLESv2, CallingConvention = CallingConvention.Winapi)]
        internal static extern void glFinish();

        // GL constants
        internal const int GL_FRAMEBUFFER = 0x8D40;
        internal const int GL_RENDERBUFFER = 0x8D41;
        internal const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
        internal const int GL_DEPTH_ATTACHMENT = 0x8D00;
        internal const int GL_STENCIL_ATTACHMENT = 0x8D20;
        internal const int GL_DEPTH24_STENCIL8 = 0x88F0;
        internal const int GL_TEXTURE_2D = 0x0DE1;
        internal const int GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
        internal const int GL_SAMPLES = 0x80A9;
        internal const int GL_TEXTURE_BINDING_2D = 0x8069;
    }
}
