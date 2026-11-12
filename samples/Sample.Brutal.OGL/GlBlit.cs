using System.Runtime.InteropServices;

/// <summary>
/// Copies a GL texture to the window's back buffer with glBlitFramebuffer, flipped to the window's top-down orientation.
/// GL state is undefined after a Skia pass, and Skia can leave the scissor test on after a clip; the blit honors it, so
/// it is turned off first.
/// </summary>
sealed class GlBlit : IDisposable
{
    const int GL_READ_FRAMEBUFFER = 0x8CA8, GL_DRAW_FRAMEBUFFER = 0x8CA9, GL_COLOR_ATTACHMENT0 = 0x8CE0;
    const int GL_TEXTURE_2D = 0x0DE1, GL_COLOR_BUFFER_BIT = 0x4000, GL_LINEAR = 0x2601, GL_SCISSOR_TEST = 0x0C11;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenFramebuffers(int n, out int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void DeleteFramebuffers(int n, ref int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindFramebuffer(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void FramebufferTexture2D(int target, int attachment, int texTarget, int texture, int level);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BlitFramebuffer(int sx0, int sy0, int sx1, int sy1, int dx0, int dy0, int dx1, int dy1, int mask, int filter);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Disable(int cap);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ColorMask(byte r, byte g, byte b, byte a);

    readonly BindFramebuffer _bind;
    readonly BlitFramebuffer _blit;
    readonly DeleteFramebuffers _delete;
    readonly Disable _disable;
    readonly ColorMask _colorMask;
    int _fbo;

    public GlBlit(Func<string, IntPtr> getProc, int texture)
    {
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(getProc(name));
        _bind = Load<BindFramebuffer>("glBindFramebuffer");
        _blit = Load<BlitFramebuffer>("glBlitFramebuffer");
        _delete = Load<DeleteFramebuffers>("glDeleteFramebuffers");
        _disable = Load<Disable>("glDisable");
        _colorMask = Load<ColorMask>("glColorMask");
        Load<GenFramebuffers>("glGenFramebuffers")(1, out _fbo);
        _bind(GL_READ_FRAMEBUFFER, _fbo);
        Load<FramebufferTexture2D>("glFramebufferTexture2D")(GL_READ_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
        _bind(GL_READ_FRAMEBUFFER, 0);
    }

    public void ToWindow(int width, int height, int windowWidth, int windowHeight)
    {
        _disable(GL_SCISSOR_TEST);
        _colorMask(1, 1, 1, 1);
        _bind(GL_READ_FRAMEBUFFER, _fbo);
        _bind(GL_DRAW_FRAMEBUFFER, 0);
        _blit(0, 0, width, height, 0, windowHeight, windowWidth, 0, GL_COLOR_BUFFER_BIT, GL_LINEAR);
        _bind(GL_READ_FRAMEBUFFER, 0);
    }

    public void Dispose()
    {
        if (_fbo != 0)
            _delete(1, ref _fbo);
    }
}
