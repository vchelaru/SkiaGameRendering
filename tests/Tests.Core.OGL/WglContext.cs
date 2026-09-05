using static Tests.CoreOgl.WglNative;

namespace Tests.CoreOgl;

/// <summary>
/// A real, hidden native window backing a real WGL OpenGL context - no swapchain, nothing ever
/// shown (this never calls <c>ShowWindow</c>; a window created without <c>WS_VISIBLE</c> is already
/// invisible). WGL has no equivalent to D3D11's WARP software device, so unlike
/// <c>Tests.Core.ANGLE</c>'s <c>WarpDevice</c>, this always talks to whatever driver
/// <c>opengl32.dll</c> resolves to via normal DLL search order - the real GPU driver on a dev box,
/// or a vendored software rasterizer dropped next to the test binary for CI (see the
/// <c>headless-gpu-testing</c> skill).
/// <para>
/// The window exists only because WGL requires an HDC from a window (or a compatible memory DC) to
/// pick a pixel format and create a context; nothing is ever drawn to it; every actual Skia draw in
/// these tests targets an FBO wrapping a separate offscreen texture, the same shape
/// <c>SkiaRenderTarget2D</c> uses.
/// </para>
/// </summary>
internal sealed class WglContext : IDisposable
{
    readonly string _className;
    readonly IntPtr _hInstance;
    readonly WndProcDelegate _wndProc;
    readonly IntPtr _hwnd;
    readonly IntPtr _hdc;
    readonly IntPtr _hglrc;

    public WglContext()
    {
        // Must happen before any GDI pixel-format call - see PreloadVendoredOpenGl32IfPresent.
        PreloadVendoredOpenGl32IfPresent();

        _hInstance = GetModuleHandleW(null);
        _className = "SkiaGameRendering.Tests.HiddenGl." + Guid.NewGuid().ToString("N");

        // Kept alive as a field: CreateWindowExW/DefWindowProcW hold onto this via the function
        // pointer registered below, and a delegate with nothing pinning it to a GC root can be
        // collected out from under that native pointer.
        _wndProc = DefWindowProcW;

        var wndClass = new WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            style = CS_OWNDC,
            lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = _className,
        };
        if (RegisterClassExW(ref wndClass) == 0)
            throw new InvalidOperationException("RegisterClassExW failed.");

        _hwnd = CreateWindowExW(0, _className, _className, WS_POPUP,
            0, 0, 4, 4, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            UnregisterClassW(_className, _hInstance);
            throw new InvalidOperationException("CreateWindowExW failed.");
        }

        _hdc = GetDC(_hwnd);
        if (_hdc == IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            UnregisterClassW(_className, _hInstance);
            throw new InvalidOperationException("GetDC failed.");
        }

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = PFD_TYPE_RGBA,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = PFD_MAIN_PLANE,
        };

        var format = ChoosePixelFormat(_hdc, ref pfd);
        if (format == 0)
        {
            ReleaseDC(_hwnd, _hdc);
            DestroyWindow(_hwnd);
            UnregisterClassW(_className, _hInstance);
            throw new InvalidOperationException("ChoosePixelFormat failed.");
        }

        if (!SetPixelFormat(_hdc, format, ref pfd))
        {
            ReleaseDC(_hwnd, _hdc);
            DestroyWindow(_hwnd);
            UnregisterClassW(_className, _hInstance);
            throw new InvalidOperationException("SetPixelFormat failed.");
        }

        _hglrc = wglCreateContext(_hdc);
        if (_hglrc == IntPtr.Zero)
        {
            ReleaseDC(_hwnd, _hdc);
            DestroyWindow(_hwnd);
            UnregisterClassW(_className, _hInstance);
            throw new InvalidOperationException("wglCreateContext failed.");
        }

        if (!wglMakeCurrent(_hdc, _hglrc))
        {
            wglDeleteContext(_hglrc);
            ReleaseDC(_hwnd, _hdc);
            DestroyWindow(_hwnd);
            UnregisterClassW(_className, _hInstance);
            throw new InvalidOperationException("wglMakeCurrent failed.");
        }
    }

    public void Dispose()
    {
        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(_hglrc);
        ReleaseDC(_hwnd, _hdc);
        DestroyWindow(_hwnd);
        UnregisterClassW(_className, _hInstance);
    }
}
