using Godot;
using SkiaGameRendering.Core.VK;
using SkiaSharp;

namespace SkiaGameRendering.Godot
{
    /// <summary>
    /// The Vulkan backend: hands Godot's own <c>VkInstance</c>/<c>VkPhysicalDevice</c>/<c>VkDevice</c>/
    /// <c>VkQueue</c> to <see cref="VkSkiaSurfaceFactory"/> and wraps the RD texture's <c>VkImage</c> for
    /// Skia to draw into directly - zero-copy, the same shape as the Stride Vulkan adapter. See
    /// <see cref="RenderingDeviceGodotBackend"/> for what the two RD backends share.
    ///
    /// MAINTENANCE NOTES (read from Godot 4.7.2's source, not assumed):
    /// <list type="bullet">
    /// <item>
    /// <b>Godot's layout bookkeeping is kept truthful with three pieces</b>, all verified clean under
    /// Godot's <c>--gpu-validation</c> (Khronos validation layer): <c>RenderingDeviceGodotBackend.PrimeForSampling</c>
    /// makes Godot's single <c>UNDEFINED -> SHADER_READ_ONLY_OPTIMAL</c> transition happen before Skia
    /// draws (a transition FROM <c>UNDEFINED</c> may discard contents, which tiled/mobile drivers
    /// do); <see cref="HandBack"/> submits an explicit <c>COLOR_ATTACHMENT_OPTIMAL -> SHADER_READ_ONLY_OPTIMAL</c>
    /// barrier after every Skia flush, because Skia leaves a wrapped render target in
    /// <c>COLOR_ATTACHMENT_OPTIMAL</c> and SkiaSharp 3.119.4 cannot be asked for anything else (see
    /// <see cref="VkSkiaSurfaceFactory.EndDraw(bool)"/>); and <see cref="Resources"/> re-wraps the surface each
    /// frame with that layout as Skia's starting point and records a no-op draw so even a frame with
    /// no other Skia work executes a render pass.
    /// </item>
    /// <item>
    /// <b>The transfer-bit landmine Core.VK warns about is satisfied through RD usage bits.</b>
    /// Skia requires <c>VK_IMAGE_USAGE_TRANSFER_SRC_BIT | TRANSFER_DST_BIT</c> on any wrapped image;
    /// Godot's Vulkan driver maps <see cref="RenderingDevice.TextureUsageBits.CanCopyFromBit"/> to
    /// <c>TRANSFER_SRC</c> and <c>CanCopyToBit</c> to <c>TRANSFER_DST</c> (<c>texture_create</c> in
    /// <c>rendering_device_driver_vulkan.cpp</c>). <see cref="ImageUsageFlags"/> mirrors that mapping
    /// back to Skia, since RD does not expose the <c>VkImageUsageFlags</c> it actually used.
    /// </item>
    /// <item>
    /// <b>API version.</b> Godot creates its instance against <c>VK_API_VERSION_1_2</c> whenever the
    /// loader is newer than 1.0 (<c>rendering_context_driver_vulkan.cpp</c>,
    /// <c>application_api_version</c>) and does not expose that number, so this clamps
    /// <see cref="VkSkiaSurfaceFactory.QueryApiVersion"/>'s loader/device answer to 1.2 rather than
    /// let Skia assume 1.3 core entry points Godot never declared.
    /// </item>
    /// <item>
    /// <b>Skia submits to Godot's main <c>VkQueue</c> without Godot's lock.</b> Godot guards each
    /// <c>vkQueueSubmit</c> with a per-queue mutex that C# cannot reach, because its texture/buffer
    /// upload workers (<c>RenderingDevice::_acquire_transfer_worker</c>) can submit from any thread.
    /// Godot creates one queue per family and puts those uploads on the family with the fewest
    /// flags that include <c>TRANSFER</c>. On most discrete GPUs that is a dedicated transfer
    /// family, a separate <c>VkQueue</c>, and nothing races. When no such family exists (typical of
    /// integrated, mobile and MoltenVK devices) uploads share Skia's queue, and a texture upload on a
    /// worker thread (threaded resource loading) can race Skia's submit.
    /// <see cref="GodotVulkanQueueFamilies"/> mirrors Godot's pick, and <see cref="InitializeCore"/> warns
    /// once when they share.
    /// </item>
    /// <item>
    /// <b>Device features.</b> <c>VkPhysicalDeviceFeatures2</c> is left null so Skia queries what the
    /// physical device supports. Godot enables every core <c>VkPhysicalDeviceFeatures</c> member it
    /// finds supported that Skia cares about (the <c>VK_DEVICEFEATURE_ENABLE_IF</c> block in
    /// <c>rendering_device_driver_vulkan.cpp</c>), so "supported" and "enabled" coincide for those,
    /// the same argument the Stride adapter makes. Extension lists are passed empty, as there too.
    /// </item>
    /// </list>
    /// </summary>
    internal sealed class VulkanGodotBackend : RenderingDeviceGodotBackend
    {
        const uint ImageUsageFlags =
            VkConstants.ImageUsageTransferSrc | VkConstants.ImageUsageTransferDst |
            VkConstants.ImageUsageSampled | VkConstants.ImageUsageColorAttachment;

        readonly VkSkiaSurfaceFactory _factory = new();
        VkImageLayoutTransitioner? _transitioner;

        internal override string DriverName => "vulkan";

        internal override bool RendersIntoGodotTexture => true;

        protected override void InitializeCore()
        {
            var rd = RenderingDevice;
            var instance = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.TopmostObject, default, 0);
            var physicalDevice = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.PhysicalDevice, default, 0);
            var device = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.LogicalDevice, default, 0);
            var queue = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.CommandQueue, default, 0);
            var queueFamilyIndex = (uint)rd.GetDriverResource(RenderingDevice.DriverResource.QueueFamily, default, 0);

            if (instance == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null VkInstance (DriverResource.TopmostObject).");
            if (physicalDevice == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null VkPhysicalDevice (DriverResource.PhysicalDevice).");
            if (device == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null VkDevice (DriverResource.LogicalDevice).");
            if (queue == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null VkQueue (DriverResource.CommandQueue).");

            var apiVersion = Math.Min(
                VkSkiaSurfaceFactory.QueryApiVersion(instance, physicalDevice),
                VkConstants.MakeApiVersion(1, 2));

            _factory.InitializeFromNative(
                instance, physicalDevice, device, queue,
                graphicsQueueFamilyIndex: queueFamilyIndex,
                apiVersion: apiVersion,
                instanceExtensions: [],
                deviceExtensions: [],
                acquireQueueLock: null);

            _transitioner = new VkImageLayoutTransitioner(device, queue, queueFamilyIndex);

            if (GodotVulkanQueueFamilies.UploadsShareMainQueue(VkSkiaSurfaceFactory.QueryQueueFamilyFlags(instance, physicalDevice), queueFamilyIndex))
                GD.PushWarning(
                    "SkiaGameRendering.Godot: this GPU has no dedicated Vulkan transfer queue, so Godot's texture uploads share the queue " +
                    "Skia submits to. An upload from another thread (for example ResourceLoader.LoadThreadedRequest) can then race Skia's " +
                    "submit. Avoid loading textures off the render thread while Skia draws, or use the d3d12 driver on Windows.");
        }

        internal override RenderingDeviceGpuResources CreateGpuResources(Rid texture, int width, int height, SKColorType colorType)
        {
            var image = RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.Texture, texture, 0);
            var format = (uint)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.TextureDataFormat, texture, 0);
            if (image == 0)
                throw new InvalidOperationException("Godot RenderingDevice returned a null VkImage for the Skia texture (DriverResource.Texture).");
            return new Resources(this, image, format, width, height, colorType);
        }

        internal override void WaitForPendingGpuWork() => _transitioner?.WaitForCompletion();

        /// <summary>
        /// Moves <paramref name="image"/> from the <c>COLOR_ATTACHMENT_OPTIMAL</c> Skia leaves it in
        /// back to the <c>SHADER_READ_ONLY_OPTIMAL</c> Godot's render graph expects a sampled texture
        /// to be in. Queued right after Skia's submit, so queue order puts it after the draw and
        /// before Godot's own sampling later in the frame; it does not wait.
        /// </summary>
        void HandBack(ulong image) =>
            _transitioner!.Transition(
                image,
                oldLayout: VkConstants.ImageLayoutColorAttachmentOptimal,
                newLayout: VkConstants.ImageLayoutShaderReadOnlyOptimal,
                srcStageMask: VkConstants.PipelineStageColorAttachmentOutput,
                srcAccessMask: VkConstants.AccessColorAttachmentWrite,
                dstStageMask: VkConstants.PipelineStageFragmentShader,
                dstAccessMask: VkConstants.AccessShaderRead);

        protected override void DisposeCore()
        {
            _transitioner?.Dispose();
            _transitioner = null;
            _factory.Dispose();
        }

        /// <summary>
        /// One wrapped <c>VkImage</c>. <b>The Skia surface is re-created every frame, not once:</b>
        /// Skia remembers the layout it last put the image in (<c>COLOR_ATTACHMENT_OPTIMAL</c>) and
        /// skips the transition next time if it believes nothing changed, but <see cref="HandBack"/>
        /// DID change it. Wrapping a fresh <see cref="GRBackendRenderTarget"/> at the real current
        /// layout each frame is the only way SkiaSharp 3.119.4 offers to tell Skia where the image is.
        /// A small Skia-side object rebuild per frame, not a GPU allocation - the <c>VkImage</c> is
        /// Godot's and persists.
        /// <para>
        /// <b>Every frame draws at least one thing.</b> <see cref="BeginFrame"/> records a 1x1
        /// <see cref="SKBlendMode.Modulate"/>-by-white rectangle - visually a no-op (dst * 1 = dst, exact
        /// in 8-bit) that Skia does not cull the way it culls <c>Dst</c>/zero-alpha paints - before the
        /// caller gets the canvas, so a <c>Begin(clear: false)</c>/<c>End()</c> frame with no other work
        /// still executes a render pass and really ends in <c>COLOR_ATTACHMENT_OPTIMAL</c>, which is what
        /// makes the hand-back barrier's <c>oldLayout</c> truthful. A later full-surface clear folds the
        /// sentinel away and is a render pass itself.
        /// </para>
        /// </summary>
        sealed class Resources : RenderingDeviceGpuResources
        {
            static readonly SKRect SentinelRect = SKRect.Create(0, 0, 1, 1);

            readonly VulkanGodotBackend _backend;
            readonly ulong _image;
            readonly uint _format;
            readonly int _width;
            readonly int _height;
            readonly SKColorType _colorType;
            readonly SKPaint _sentinelPaint = new() { Color = SKColors.White, BlendMode = SKBlendMode.Modulate, IsAntialias = false };
            SKSurface? _surface;
            GRBackendRenderTarget? _renderTarget;

            internal Resources(VulkanGodotBackend backend, ulong image, uint format, int width, int height, SKColorType colorType)
            {
                _backend = backend;
                _image = image;
                _format = format;
                _width = width;
                _height = height;
                _colorType = colorType;
            }

            internal override SKSurface BeginFrame()
            {
                _backend._factory.BeginDraw();
                try
                {
                    DisposeSurface();
                    var state = _backend._factory.CreateTextureState(
                        _image, _format, VkConstants.ImageLayoutShaderReadOnlyOptimal, ImageUsageFlags,
                        imageTiling: 0 /* VK_IMAGE_TILING_OPTIMAL - Godot's texture_create always uses optimal tiling. */);
                    (_surface, _renderTarget) = _backend._factory.CreateSurface(state, _width, _height, _colorType);
                    _surface.Canvas.DrawRect(SentinelRect, _sentinelPaint);
                    return _surface;
                }
                catch
                {
                    _backend._factory.EndDraw(synchronous: false);
                    throw;
                }
            }

            internal override void EndFrame()
            {
                try
                {
                    if (_surface != null)
                    {
                        // Unwind any Save()/SaveLayer() the caller left open; the surface is discarded
                        // next frame anyway, but an open SaveLayer would otherwise never be composited.
                        _surface.Canvas.RestoreToCount(1);
                        _surface.Flush();
                    }
                }
                finally
                {
                    // No CPU wait: Godot's sampling of the image is queue-ordered behind this submit.
                    _backend._factory.EndDraw(synchronous: false);
                }

                _backend.HandBack(_image);
            }

            void DisposeSurface()
            {
                _surface?.Dispose();
                _surface = null;
                _renderTarget?.Dispose();
                _renderTarget = null;
            }

            public override void Dispose()
            {
                _backend._factory.BeginDraw();
                try
                {
                    DisposeSurface();
                }
                finally
                {
                    _backend._factory.EndDraw(synchronous: false);
                }
                _sentinelPaint.Dispose();
            }
        }
    }
}
