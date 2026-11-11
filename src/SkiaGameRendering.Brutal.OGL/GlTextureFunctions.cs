using System.Runtime.InteropServices;
using SkiaGameRendering.Core.OGL;

namespace SkiaGameRendering
{
    /// <summary>The texture entry points Core.OGL does not load, since its other backends are handed their textures.</summary>
    internal sealed class GlTextureFunctions
    {
        const int GL_TEXTURE_2D = 0x0DE1;
        const int GL_TEXTURE_BINDING_2D = 0x8069;
        const int GL_TEXTURE_MIN_FILTER = 0x2801;
        const int GL_TEXTURE_MAG_FILTER = 0x2800;
        const int GL_TEXTURE_WRAP_S = 0x2802;
        const int GL_TEXTURE_WRAP_T = 0x2803;
        const int GL_LINEAR = 0x2601;
        const int GL_CLAMP_TO_EDGE = 0x812F;
        const int GL_RGBA8 = 0x8058;
        const int GL_RGBA = 0x1908;
        const int GL_UNSIGNED_BYTE = 0x1401;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void GenTexturesDelegate(int count, out int textures);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void DeleteTexturesDelegate(int count, ref int textures);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void BindTextureDelegate(int target, int texture);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void TexImage2DDelegate(int target, int level, int internalFormat, int width, int height, int border,
            int format, int type, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void TexParameteriDelegate(int target, int name, int value);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        unsafe delegate void GetIntegervDelegate(int name, int* data);

        GenTexturesDelegate _genTextures = null!;
        DeleteTexturesDelegate _deleteTextures = null!;
        BindTextureDelegate _bindTexture = null!;
        TexImage2DDelegate _texImage2D = null!;
        TexParameteriDelegate _texParameteri = null!;
        GetIntegervDelegate _getIntegerv = null!;

        GlTextureFunctions() { }

        public static GlTextureFunctions Load(IGlFunctionLoader loader) => new()
        {
            _genTextures = loader.Load<GenTexturesDelegate>("glGenTextures"),
            _deleteTextures = loader.Load<DeleteTexturesDelegate>("glDeleteTextures"),
            _bindTexture = loader.Load<BindTextureDelegate>("glBindTexture"),
            _texImage2D = loader.Load<TexImage2DDelegate>("glTexImage2D"),
            _texParameteri = loader.Load<TexParameteriDelegate>("glTexParameteri"),
            _getIntegerv = loader.Load<GetIntegervDelegate>("glGetIntegerv"),
        };

        /// <summary>An empty RGBA8 texture with linear filtering and clamped edges. Leaves the app's texture binding alone.</summary>
        public unsafe int CreateRgba8(int width, int height)
        {
            int previous;
            _getIntegerv(GL_TEXTURE_BINDING_2D, &previous);
            _genTextures(1, out var texture);
            _bindTexture(GL_TEXTURE_2D, texture);
            _texImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);
            _texParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
            _texParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            _texParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            _texParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            _bindTexture(GL_TEXTURE_2D, previous);
            return texture;
        }

        /// <summary>The current viewport's width and height, which is how big the app says its draw target is.</summary>
        public unsafe (int Width, int Height) GetViewportSize()
        {
            const int GL_VIEWPORT = 0x0BA2;
            var viewport = stackalloc int[4];
            _getIntegerv(GL_VIEWPORT, viewport);
            return (viewport[2], viewport[3]);
        }

        public void Delete(int texture) => _deleteTextures(1, ref texture);
    }
}
