using System.Runtime.InteropServices;
using SkiaGameRendering.Core.OGL;

namespace Tests.CoreOgl;

/// <summary>
/// The handful of raw GL 1.1 calls <c>WglSkiaPixelReadbackTests</c> needs to create a texture and
/// read pixels back to CPU - test scaffolding only, not part of the production interop in
/// <see cref="GlFunctions"/> (production callers already have a texture; they never create one
/// themselves). Loaded through the same <see cref="IGlFunctionLoader"/> as production code, so it
/// exercises whichever driver is actually current - real GPU or a vendored software rasterizer -
/// exactly like the calls in <see cref="GlFunctions"/> do.
/// </summary>
internal sealed class GlRawTestFunctions
{
    internal const int GL_TEXTURE_2D = 0x0DE1;
    internal const int GL_RGBA = 0x1908;
    internal const int GL_RGBA8 = 0x8058;
    internal const int GL_UNSIGNED_BYTE = 0x1401;
    internal const int GL_VENDOR = 0x1F00;
    internal const int GL_RENDERER = 0x1F01;
    internal const int GL_VERSION = 0x1F02;

    /// <summary>
    /// Which framebuffer is currently bound - the one piece of GL state
    /// <c>GlSkiaSurfaceFactory.UnbindAfterDrawing</c> undertakes to put back.
    /// </summary>
    internal const int GL_FRAMEBUFFER_BINDING = 0x8CA6;

    internal delegate void GenTexturesDelegate(int n, out int textures);
    internal delegate void BindTextureDelegate(int target, int texture);
    internal delegate void DeleteTexturesDelegate(int n, ref int textures);
    internal unsafe delegate void TexImage2DDelegate(int target, int level, int internalFormat,
        int width, int height, int border, int format, int type, void* pixels);
    internal unsafe delegate void ReadPixelsDelegate(int x, int y, int width, int height,
        int format, int type, void* pixels);
    internal delegate IntPtr GetStringDelegate(int name);
    internal delegate void GetIntegervDelegate(int name, out int value);

    internal GenTexturesDelegate GenTextures { get; }
    internal BindTextureDelegate BindTexture { get; }
    internal DeleteTexturesDelegate DeleteTextures { get; }
    internal TexImage2DDelegate TexImage2D { get; }
    internal ReadPixelsDelegate ReadPixels { get; }
    internal GetStringDelegate GetString { get; }
    internal GetIntegervDelegate GetIntegerv { get; }

    internal GlRawTestFunctions(IGlFunctionLoader loader)
    {
        GenTextures = loader.Load<GenTexturesDelegate>("glGenTextures");
        BindTexture = loader.Load<BindTextureDelegate>("glBindTexture");
        DeleteTextures = loader.Load<DeleteTexturesDelegate>("glDeleteTextures");
        TexImage2D = loader.Load<TexImage2DDelegate>("glTexImage2D");
        ReadPixels = loader.Load<ReadPixelsDelegate>("glReadPixels");
        GetString = loader.Load<GetStringDelegate>("glGetString");
        GetIntegerv = loader.Load<GetIntegervDelegate>("glGetIntegerv");
    }

    internal string GetStringUtf8(int name) =>
        Marshal.PtrToStringAnsi(GetString(name)) ?? string.Empty;
}
