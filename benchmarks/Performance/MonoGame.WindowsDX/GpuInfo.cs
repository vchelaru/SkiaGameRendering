using Microsoft.Xna.Framework.Graphics;

namespace Performance
{
    public static class GpuInfo
    {
        // The DXGI adapter the device was created on, which is the GPU that actually ran on a
        // hybrid-graphics laptop.
        public static string Query(GraphicsDevice device) => device.Adapter.Description;
    }
}
