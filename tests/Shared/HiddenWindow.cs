using System.Runtime.InteropServices;

namespace Tests.Shared;

/// <summary>
/// A real native window that is never shown - created without <c>WS_VISIBLE</c> and never passed to
/// <c>ShowWindow</c>. Windows graphics APIs keep asking for an HWND even when nothing is meant to
/// appear on screen: WGL needs one for the HDC it picks a pixel format from, and MonoGame's D3D11
/// device creates a swap chain against <c>PresentationParameters.DeviceWindowHandle</c> whether or
/// not anything is ever presented. This satisfies both without a desktop the test can see.
/// <para>
/// Nothing is ever drawn to the window itself. Every render in these tests targets an offscreen
/// texture, the same shape <c>SkiaRenderTarget2D</c> uses.
/// </para>
/// </summary>
sealed class HiddenWindow : IDisposable
{
    const uint WS_POPUP = 0x80000000;
    const uint CS_OWNDC = 0x0020;

    readonly string _className;
    readonly IntPtr _hInstance;

    // Kept alive as a field: CreateWindowExW holds this via the function pointer registered below,
    // and a delegate with nothing pinning it to a GC root can be collected out from under it.
    readonly WndProcDelegate _wndProc;

    internal IntPtr Handle { get; }

    internal HiddenWindow(int width = 4, int height = 4)
    {
        _hInstance = GetModuleHandleW(null);
        _className = "SkiaGameRendering.Tests.Hidden." + Guid.NewGuid().ToString("N");
        _wndProc = DefWindowProcW;

        var wndClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = CS_OWNDC,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _hInstance,
            lpszClassName = _className,
        };
        if (RegisterClassExW(ref wndClass) == 0)
            throw new InvalidOperationException("RegisterClassExW failed.");

        Handle = CreateWindowExW(0, _className, _className, WS_POPUP,
            0, 0, width, height, IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            UnregisterClassW(_className, _hInstance);
            throw new InvalidOperationException("CreateWindowExW failed.");
        }
    }

    public void Dispose()
    {
        DestroyWindow(Handle);
        UnregisterClassW(_className, _hInstance);
    }

    internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
