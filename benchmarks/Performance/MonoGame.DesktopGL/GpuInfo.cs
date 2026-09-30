using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;

namespace Performance
{
    public static class GpuInfo
    {
        private const uint GL_RENDERER = 0x1F01;

        private static readonly string[] GlLibraries =
        {
            "opengl32.dll",
            "/System/Library/Frameworks/OpenGL.framework/OpenGL",
            "libGL.so.1",
        };

        private delegate IntPtr GlGetString(uint name);

        /// <summary>
        /// GL_RENDERER of the current context, which names the GPU that actually ran on a
        /// hybrid-graphics laptop. Must be called on the game thread while MonoGame's context is current.
        /// </summary>
        public static string Query(GraphicsDevice device)
        {
            foreach (var library in GlLibraries)
            {
                if (NativeLibrary.TryLoad(library, out var handle)
                    && NativeLibrary.TryGetExport(handle, "glGetString", out var export))
                {
                    var getString = Marshal.GetDelegateForFunctionPointer<GlGetString>(export);
                    string? renderer = Marshal.PtrToStringAnsi(getString(GL_RENDERER));
                    if (!string.IsNullOrEmpty(renderer))
                        return renderer;
                }
            }
            return device.Adapter.Description;
        }
    }
}
