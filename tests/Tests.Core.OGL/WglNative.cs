using System.Runtime.InteropServices;

namespace Tests.CoreOgl;

/// <summary>
/// Raw Win32/WGL P/Invoke declarations backing <see cref="WglContext"/> and
/// <see cref="WglFunctionLoader"/> - the standard "hidden window + dummy WGL context" recipe used
/// throughout the graphics industry to get a real OpenGL context with nothing on screen. Test
/// scaffolding only, not part of the production interop in <c>SkiaGameRendering.Core.OGL</c>.
/// </summary>
internal static class WglNative
{
    /// <summary>
    /// Explicitly preloads a vendored <c>opengl32.dll</c> sitting next to the test binary, if one is
    /// there, before any GDI pixel-format call - <see cref="WglContext"/> calls this first thing.
    /// <para>
    /// A plain <c>DllImport("opengl32.dll")</c> is not enough by itself: GDI's own
    /// <c>ChoosePixelFormat</c>/<c>SetPixelFormat</c> (see <c>WglContext</c>) resolve their own
    /// internal reference to <c>opengl32.dll</c> independently of anything this assembly does, and on
    /// modern Windows that resolution is hardened to always come from System32 - so a vendored copy
    /// sitting next to the test binary loses to the real driver for GDI's half even when a later
    /// direct <c>DllImport("opengl32.dll")</c> call from our own code would have found the local one.
    /// The one loophole: Windows always reuses an already-loaded module that matches by file name,
    /// regardless of where it was loaded from or which search rules would otherwise apply - the same
    /// mechanism DLL-proxying/hijacking exploits. Loading the vendored copy here, before GDI ever
    /// touches "opengl32.dll" itself, makes GDI's later internal resolution reuse this module too, so
    /// <c>ChoosePixelFormat</c> and <c>wglCreateContext</c> end up talking to the same driver.
    /// </para>
    /// </summary>
    internal static void PreloadVendoredOpenGl32IfPresent()
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "opengl32.dll");
        if (File.Exists(localPath))
            NativeLibrary.TryLoad(localPath, out _);
    }

    internal const uint WS_POPUP = 0x80000000;
    internal const uint CS_OWNDC = 0x0020;

    internal const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    internal const uint PFD_SUPPORT_OPENGL = 0x00000020;
    internal const uint PFD_DOUBLEBUFFER = 0x00000001;
    internal const byte PFD_TYPE_RGBA = 0;
    internal const byte PFD_MAIN_PLANE = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits;
        public byte cRedShift;
        public byte cGreenBits;
        public byte cGreenShift;
        public byte cBlueBits;
        public byte cBlueShift;
        public byte cAlphaBits;
        public byte cAlphaShift;
        public byte cAccumBits;
        public byte cAccumRedBits;
        public byte cAccumGreenBits;
        public byte cAccumBlueBits;
        public byte cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask;
        public uint dwVisibleMask;
        public uint dwDamageMask;
    }

    internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    // user32.dll
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    // kernel32.dll
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    internal static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // gdi32.dll
    [DllImport("gdi32.dll")]
    internal static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("gdi32.dll")]
    internal static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

    // opengl32.dll - wglGetProcAddress only resolves entry points beyond OpenGL 1.1; everything
    // else (including these three context-management calls) is loaded by ordinary DLL import
    // instead, same as any other opengl32.dll export.
    [DllImport("opengl32.dll")]
    internal static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll")]
    internal static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [DllImport("opengl32.dll")]
    internal static extern bool wglDeleteContext(IntPtr hglrc);

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    internal static extern IntPtr wglGetProcAddress(string lpszProc);
}
