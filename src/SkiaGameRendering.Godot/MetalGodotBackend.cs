using Godot;
using SkiaGameRendering.Core.Metal;
using SkiaSharp;

namespace SkiaGameRendering.Godot
{
    /// <summary>
    /// The Metal backend (macOS): hands Godot's <c>id&lt;MTLDevice&gt;</c>/<c>id&lt;MTLCommandQueue&gt;</c>
    /// to <see cref="MetalSkiaSurfaceFactory"/> and wraps the RD texture's <c>id&lt;MTLTexture&gt;</c>
    /// for Skia to draw into directly - zero-copy. See <see cref="RenderingDeviceGodotBackend"/> for
    /// what the RD backends share.
    ///
    /// MAINTENANCE NOTES (read from Godot 4.7.2's source, not assumed):
    /// <list type="bullet">
    /// <item>
    /// <b>Much simpler than Vulkan and D3D12.</b> Metal textures have no layout or state, so there is
    /// nothing to hand back after a draw, and the Skia surface can live as long as the target. Godot
    /// creates RD textures with typed <c>MTLPixelFormat</c>s and adds <c>MTLTextureUsageRenderTarget</c>
    /// for <c>ColorAttachmentBit</c> (<c>texture_create</c> in <c>rendering_device_driver_metal.cpp</c>).
    /// </item>
    /// <item>
    /// <b>Ordering comes from Metal's hazard tracking.</b> Godot's Metal 3 driver creates tracked
    /// resources unless <c>GODOT_MTL_FORCE_BARRIERS=1</c> is set (<c>initialize</c> in
    /// <c>rendering_device_driver_metal3.cpp</c>), and Skia commits to Godot's own queue, so Godot's
    /// later sampling waits for Skia's writes without help. Under forced barriers the texture is
    /// untracked and nothing orders the two, so this backend then waits on the CPU after every draw.
    /// </item>
    /// </list>
    /// </summary>
    internal sealed class MetalGodotBackend : RenderingDeviceGodotBackend
    {
        readonly MetalSkiaSurfaceFactory _factory = new();
        bool _untrackedResources;

        internal override string DriverName => "metal";

        internal override bool RendersIntoGodotTexture => true;

        protected override void InitializeCore()
        {
            var rd = RenderingDevice;
            var device = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.LogicalDevice, default, 0);
            var queue = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.CommandQueue, default, 0);

            if (device == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null MTLDevice (DriverResource.LogicalDevice).");
            if (queue == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null MTLCommandQueue (DriverResource.CommandQueue).");

            _factory.InitializeFromNative(device, queue);
            _untrackedResources = OS.GetEnvironment("GODOT_MTL_FORCE_BARRIERS") == "1";
        }

        internal override RenderingDeviceGpuResources CreateGpuResources(Rid texture, int width, int height, SKColorType colorType)
        {
            var mtlTexture = (IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.Texture, texture, 0);
            if (mtlTexture == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null MTLTexture for the Skia texture (DriverResource.Texture).");
            return new Resources(this, _factory.CreateTextureState(mtlTexture), width, height, colorType);
        }

        internal override void WaitForPendingGpuWork() => _factory.EndDraw(synchronous: true);

        protected override void DisposeCore() => _factory.Dispose();

        sealed class Resources : RenderingDeviceGpuResources
        {
            readonly MetalGodotBackend _backend;
            readonly SKSurface _surface;
            readonly GRBackendRenderTarget _renderTarget;

            internal Resources(MetalGodotBackend backend, MetalTextureState state, int width, int height, SKColorType colorType)
            {
                _backend = backend;
                (_surface, _renderTarget) = backend._factory.CreateSurface(state, width, height, colorType);
            }

            internal override SKSurface BeginFrame()
            {
                _backend._factory.BeginDraw();
                return _surface;
            }

            internal override void EndFrame()
            {
                try
                {
                    // Unwind any Save()/SaveLayer() the caller left open so it doesn't leak into the next frame.
                    _surface.Canvas.RestoreToCount(1);
                    _surface.Flush();
                }
                finally
                {
                    _backend._factory.EndDraw(synchronous: _backend._untrackedResources);
                }
            }

            public override void Dispose()
            {
                _surface.Dispose();
                _renderTarget.Dispose();
            }
        }
    }
}
