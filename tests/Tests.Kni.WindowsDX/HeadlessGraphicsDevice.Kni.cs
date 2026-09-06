using Microsoft.Xna.Framework.Graphics;

namespace Tests.Shared;

partial class HeadlessGraphicsDevice
{
    /// <summary>KNI carries the same knob on the presentation parameters rather than on the adapter.</summary>
    static partial void PinToSoftwareRasterizer(PresentationParameters presentationParameters) =>
        presentationParameters.UseDriverType = PresentationParameters.DriverType.FastSoftware;
}
