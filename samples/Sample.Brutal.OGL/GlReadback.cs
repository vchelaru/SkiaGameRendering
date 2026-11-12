using System.Runtime.InteropServices;

/// <summary>Test-only readback, so the smoke test can assert pixels without any library CPU path.</summary>
static unsafe class GlReadback
{
    const int GL_FRAMEBUFFER = 0x8D40;
    const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
    const int GL_TEXTURE_2D = 0x0DE1;
    const int GL_RGBA = 0x1908;
    const int GL_UNSIGNED_BYTE = 0x1401;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenFramebuffers(int n, out int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindFramebuffer(int target, int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void FramebufferTexture2D(int target, int attachment, int texTarget, int texture, int level);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ReadPixels(int x, int y, int w, int h, int format, int type, byte* data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void DeleteFramebuffers(int n, ref int ids);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void PixelStorei(int name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindBuffer(int target, int buffer);

    // GL state is undefined after a Skia pass; glReadPixels depends on the pack settings and the pack buffer binding.
    static void ResetPackState(Func<string, IntPtr> getProc)
    {
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(getProc(name));
        Load<BindBuffer>("glBindBuffer")(0x88EB /* PIXEL_PACK_BUFFER */, 0);
        var pixelStore = Load<PixelStorei>("glPixelStorei");
        pixelStore(0x0D05 /* PACK_ALIGNMENT */, 4);
        pixelStore(0x0D02 /* PACK_ROW_LENGTH */, 0);
        pixelStore(0x0D03 /* PACK_SKIP_ROWS */, 0);
        pixelStore(0x0D04 /* PACK_SKIP_PIXELS */, 0);
    }

    /// <summary>RGBA bytes of the default framebuffer (the back buffer), flipped so row 0 is the top as a window shows it.</summary>
    public static byte[] ReadDefaultFramebuffer(Func<string, IntPtr> getProc, int width, int height)
    {
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(getProc(name));
        ResetPackState(getProc);
        Load<BindFramebuffer>("glBindFramebuffer")(GL_FRAMEBUFFER, 0);
        var bottomUp = new byte[width * height * 4];
        fixed (byte* p = bottomUp)
            Load<ReadPixels>("glReadPixels")(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, p);
        var rgba = new byte[bottomUp.Length];
        var stride = width * 4;
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(bottomUp, (height - 1 - y) * stride, rgba, y * stride, stride);
        return rgba;
    }

    /// <summary>RGBA bytes of a texture, row 0 = texel row 0 (the top of the canvas).</summary>
    public static byte[] ReadTexture(Func<string, IntPtr> getProc, int texture, int width, int height)
    {
        T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(getProc(name));
        ResetPackState(getProc);
        Load<GenFramebuffers>("glGenFramebuffers")(1, out var fbo);
        Load<BindFramebuffer>("glBindFramebuffer")(GL_FRAMEBUFFER, fbo);
        Load<FramebufferTexture2D>("glFramebufferTexture2D")(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
        var rgba = new byte[width * height * 4];
        fixed (byte* p = rgba)
            Load<ReadPixels>("glReadPixels")(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Load<BindFramebuffer>("glBindFramebuffer")(GL_FRAMEBUFFER, 0);
        Load<DeleteFramebuffers>("glDeleteFramebuffers")(1, ref fbo);
        return rgba;
    }
}
