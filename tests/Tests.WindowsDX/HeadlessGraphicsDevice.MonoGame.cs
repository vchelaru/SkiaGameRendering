using Microsoft.Xna.Framework.Graphics;

namespace Tests.Shared;

partial class HeadlessGraphicsDevice
{
    /// <summary>
    /// MonoGame reads this static while creating the D3D11 device, ignoring the
    /// <see cref="GraphicsAdapter"/> it was handed - so the parameter is unused here.
    /// </summary>
    static partial void PinToSoftwareRasterizer(PresentationParameters presentationParameters) =>
        GraphicsAdapter.UseDriverType = GraphicsAdapter.DriverType.FastSoftware;
}
