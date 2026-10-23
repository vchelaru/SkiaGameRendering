using Microsoft.Xna.Framework.Graphics;

namespace Performance
{
    public static class GpuInfo
    {
        /// <summary>The browser reports the GPU through WEBGL_debug_renderer_info; the page puts it in <see cref="PerfEnvironment.Gpu"/>.</summary>
        public static string Query(GraphicsDevice device) => PerfEnvironment.Gpu ?? device.Adapter.Description;
    }
}
