using Tests.Shared;
using static Tests.CoreOgl.WglNative;

namespace Tests.CoreOgl;

/// <summary>
/// A real WGL OpenGL context on a <see cref="HiddenWindow"/> - no swapchain, nothing ever shown.
/// WGL has no equivalent to D3D11's WARP software device, so unlike <c>Tests.Core.ANGLE</c>'s
/// <c>WarpDevice</c>, this always talks to whatever driver <c>opengl32.dll</c> resolves to via
/// normal DLL search order - the real GPU driver on a dev box, or a vendored software rasterizer
/// dropped next to the test binary for CI (see the <c>headless-gpu-testing</c> skill).
/// <para>
/// The window exists only because WGL requires an HDC from a window (or a compatible memory DC) to
/// pick a pixel format and create a context; nothing is ever drawn to it; every actual Skia draw in
/// these tests targets an FBO wrapping a separate offscreen texture, the same shape
/// <c>SkiaRenderTarget2D</c> uses.
/// </para>
/// </summary>
internal sealed class WglContext : IDisposable
{
    readonly HiddenWindow _window;
    readonly IntPtr _hdc;
    readonly IntPtr _hglrc;

    public WglContext()
    {
        // Must happen before any GDI pixel-format call - see VendoredOpenGl.PreloadIfPresent.
        VendoredOpenGl.PreloadIfPresent();

        _window = new HiddenWindow();

        _hdc = GetDC(_window.Handle);
        if (_hdc == IntPtr.Zero)
        {
            _window.Dispose();
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
            ReleaseAll();
            throw new InvalidOperationException("ChoosePixelFormat failed.");
        }

        if (!SetPixelFormat(_hdc, format, ref pfd))
        {
            ReleaseAll();
            throw new InvalidOperationException("SetPixelFormat failed.");
        }

        _hglrc = wglCreateContext(_hdc);
        if (_hglrc == IntPtr.Zero)
        {
            ReleaseAll();
            throw new InvalidOperationException("wglCreateContext failed.");
        }

        if (!wglMakeCurrent(_hdc, _hglrc))
        {
            wglDeleteContext(_hglrc);
            ReleaseAll();
            throw new InvalidOperationException("wglMakeCurrent failed.");
        }
    }

    public void Dispose()
    {
        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(_hglrc);
        ReleaseAll();
    }

    void ReleaseAll()
    {
        ReleaseDC(_window.Handle, _hdc);
        _window.Dispose();
    }
}
