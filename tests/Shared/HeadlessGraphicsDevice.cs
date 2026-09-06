using Microsoft.Xna.Framework.Graphics;

namespace Tests.Shared;

/// <summary>
/// A live <see cref="GraphicsDevice"/> with no <c>Game</c>, no game loop and nothing on screen -
/// just a <see cref="HiddenWindow"/> for the swap chain to attach to. That is enough for everything
/// above <c>Core.ANGLE</c>: <c>SkiaRenderTarget2D</c>, the engine-side texture sharing and the state
/// handling around a Skia draw all work off the device, not the window. Linked into both the
/// MonoGame and the KNI WindowsDX test project - the constructor below compiles unchanged against
/// either engine's assembly.
/// <para>
/// The device is pinned to WARP (Windows' bundled software D3D11 rasterizer) rather than whatever
/// GPU the machine has, which both engines expose as a public knob. That keeps device creation off
/// the question of whether the machine has a hardware adapter at all, and puts these goldens on the
/// same rasterizer <c>Tests.Core.ANGLE</c>'s came from, so they compare everywhere with none of the
/// environment gating the OpenGL and Vulkan goldens need.
/// </para>
/// </summary>
sealed partial class HeadlessGraphicsDevice : IDisposable
{
    readonly HiddenWindow _window;

    internal GraphicsDevice GraphicsDevice { get; }

    internal HeadlessGraphicsDevice(int width, int height)
    {
        _window = new HiddenWindow(width, height);
        try
        {
            var presentationParameters = new PresentationParameters
            {
                BackBufferWidth = width,
                BackBufferHeight = height,
                BackBufferFormat = SurfaceFormat.Color,
                DepthStencilFormat = DepthFormat.Depth24Stencil8,
                DeviceWindowHandle = _window.Handle,
                IsFullScreen = false,
                PresentationInterval = PresentInterval.Immediate,
            };
            PinToSoftwareRasterizer(presentationParameters);

            GraphicsDevice = new GraphicsDevice(
                GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef, presentationParameters);
        }
        catch
        {
            _window.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asks the engine for a WARP device instead of a hardware one. MonoGame and KNI both offer
    /// this, in different places, so each test project implements it against its own engine rather
    /// than this file guessing - see the <c>HeadlessGraphicsDevice.*.cs</c> next to each.
    /// </summary>
    static partial void PinToSoftwareRasterizer(PresentationParameters presentationParameters);

    public void Dispose()
    {
        GraphicsDevice.Dispose();
        _window.Dispose();
    }
}
