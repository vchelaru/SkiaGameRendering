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

    // user32.dll
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
