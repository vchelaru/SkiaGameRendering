using System.Runtime.InteropServices;

namespace SkiaGameRendering
{
    /// <summary>
    /// Puts back the GLES state that MonoGame and KNI assume but never cache, after Skia drew on the engine's
    /// own context. Neither engine uses vertex array or sampler objects, so both go back to none; neither
    /// re-enables a vertex attribute it thinks is enabled, so all are disabled and the engine's record of
    /// them is cleared separately; and uploads assume the default pixel-store settings. Everything the
    /// engines do cache (blend, depth, program, buffers, textures, viewport) is re-applied by the engine,
    /// see <c>MonoGameStateInvalidation</c> and <c>KniAndroidStateInvalidation</c>.
    /// </summary>
    internal sealed class GlesEngineStateReset
    {
        private const string Gles = "libGLESv2.so";

        private const int GL_VERSION = 0x1F02;
        private const int GL_MAX_VERTEX_ATTRIBS = 0x8869;
        private const int GL_MAX_COMBINED_TEXTURE_IMAGE_UNITS = 0x8B4D;
        private const int GL_ARRAY_BUFFER = 0x8892;
        private const int GL_ELEMENT_ARRAY_BUFFER = 0x8893;
        private const int GL_PIXEL_PACK_BUFFER = 0x88EB;
        private const int GL_PIXEL_UNPACK_BUFFER = 0x88EC;
        private const int GL_FRAMEBUFFER = 0x8D40;
        private const int GL_RENDERBUFFER = 0x8D41;
        private const int GL_TEXTURE0 = 0x84C0;
        private const int GL_UNPACK_ALIGNMENT = 0x0CF5;
        private const int GL_PACK_ALIGNMENT = 0x0D05;
        private const int GL_UNPACK_ROW_LENGTH = 0x0CF2;
        private const int GL_UNPACK_SKIP_ROWS = 0x0CF3;
        private const int GL_UNPACK_SKIP_PIXELS = 0x0CF4;
        private const int GL_PACK_ROW_LENGTH = 0x0D02;
        private const int GL_PACK_SKIP_ROWS = 0x0D03;
        private const int GL_PACK_SKIP_PIXELS = 0x0D04;

        // Skia binds at most a handful of texture units; the engines' own bindings are re-applied by them.
        private const int MaxSamplerUnits = 16;

        [DllImport(Gles)] private static extern void glGetIntegerv(int pname, out int data);
        [DllImport(Gles)] private static extern IntPtr glGetString(int name);
        [DllImport(Gles)] private static extern void glBindFramebuffer(int target, int framebuffer);
        [DllImport(Gles)] private static extern void glBindRenderbuffer(int target, int renderbuffer);
        [DllImport(Gles)] private static extern void glBindBuffer(int target, int buffer);
        [DllImport(Gles)] private static extern void glBindVertexArray(int array);
        [DllImport(Gles)] private static extern void glBindSampler(int unit, int sampler);
        [DllImport(Gles)] private static extern void glDisableVertexAttribArray(int index);
        [DllImport(Gles)] private static extern void glVertexAttribDivisor(int index, int divisor);
        [DllImport(Gles)] private static extern void glActiveTexture(int texture);
        [DllImport(Gles)] private static extern void glPixelStorei(int pname, int param);

        private bool _queried;
        private bool _gles3;
        private int _vertexAttribs;
        private int _samplerUnits;

        /// <summary>Called on the engine's context, with Skia's draws already submitted.</summary>
        internal void Reset()
        {
            if (!_queried)
            {
                // "OpenGL ES 3.2 ..."; GLES 2 has no vertex array objects, samplers, divisors or row lengths.
                var version = Marshal.PtrToStringAnsi(glGetString(GL_VERSION)) ?? "";
                _gles3 = version.StartsWith("OpenGL ES ", StringComparison.Ordinal) && version.Length > 10 && version[10] >= '3';
                glGetIntegerv(GL_MAX_VERTEX_ATTRIBS, out _vertexAttribs);
                glGetIntegerv(GL_MAX_COMBINED_TEXTURE_IMAGE_UNITS, out _samplerUnits);
                _samplerUnits = Math.Min(_samplerUnits, MaxSamplerUnits);
                _queried = true;
            }

            glBindFramebuffer(GL_FRAMEBUFFER, 0);
            glBindRenderbuffer(GL_RENDERBUFFER, 0);
            if (_gles3)
            {
                glBindVertexArray(0);
                for (var unit = 0; unit < _samplerUnits; unit++)
                    glBindSampler(unit, 0);
                glBindBuffer(GL_PIXEL_PACK_BUFFER, 0);
                glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
                glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
                glPixelStorei(GL_UNPACK_SKIP_ROWS, 0);
                glPixelStorei(GL_UNPACK_SKIP_PIXELS, 0);
                glPixelStorei(GL_PACK_ROW_LENGTH, 0);
                glPixelStorei(GL_PACK_SKIP_ROWS, 0);
                glPixelStorei(GL_PACK_SKIP_PIXELS, 0);
            }
            glBindBuffer(GL_ARRAY_BUFFER, 0);
            glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0);
            for (var index = 0; index < _vertexAttribs; index++)
            {
                glDisableVertexAttribArray(index);
                if (_gles3)
                    glVertexAttribDivisor(index, 0);
            }
            glActiveTexture(GL_TEXTURE0);
            glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
            glPixelStorei(GL_PACK_ALIGNMENT, 4);
        }
    }
}
