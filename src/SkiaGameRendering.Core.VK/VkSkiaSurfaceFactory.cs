using SkiaSharp;

namespace SkiaGameRendering.Core.VK
{
    /// <summary>
    /// A single host-supplied <c>VkImage</c> wrapped for Skia, plus the Vulkan metadata Skia needs
    /// to draw into it. Returned by <see cref="VkSkiaSurfaceFactory.CreateTextureState"/> and
    /// consumed by <see cref="VkSkiaSurfaceFactory.CreateSurface"/>.
    /// </summary>
    public sealed class VkTextureState
    {
        internal VkTextureState(ulong vkImage, GRVkImageInfo imageInfo)
        {
            VkImage = vkImage;
            ImageInfo = imageInfo;
        }

        /// <summary>The wrapped <c>VkImage</c> handle, for the host's own bookkeeping.</summary>
        public ulong VkImage { get; }

        internal GRVkImageInfo ImageInfo { get; }
    }

    /// <summary>
    /// Engine-agnostic Vulkan/Skia interop, shared by any host engine that renders via Vulkan
    /// (Stride on Linux/macOS today - Vulkan is its only graphics platform there, since it dropped
    /// OpenGL entirely; see issue #23).
    ///
    /// How it works: unlike <c>Core.ANGLE</c>, which creates its own EGL context/device wrapping the
    /// host's D3D11 device, <c>Core.VK</c> creates nothing - the host engine already has a
    /// <c>VkInstance</c>/<c>VkPhysicalDevice</c>/<c>VkDevice</c>/<c>VkQueue</c>, and Skia's
    /// <c>GrVkGpu</c> is handed those same raw handles directly via
    /// <see cref="SkiaSharp.GRVkBackendContext"/>. Skia's draw commands and the host's own commands
    /// both submit to the SAME <c>VkQueue</c> - there is no separate context to own or texture-import
    /// step the way ANGLE needs <c>eglCreateDeviceANGLE</c>/<c>eglCreatePbufferFromClientBuffer</c>;
    /// wrapping a <c>VkImage</c> is just constructing a <see cref="SkiaSharp.GRVkImageInfo"/> that
    /// describes it (see <see cref="CreateTextureState"/>).
    ///
    /// MAINTENANCE NOTES:
    /// <list type="bullet">
    /// <item>
    /// <b>No interop library on either side.</b> <see cref="InitializeFromNative"/> takes the host's
    /// Vulkan handles as raw <see cref="IntPtr"/>s (and the queue family index / API version / VkImage
    /// fields as raw integers) - no PackageReference to Silk.NET.Vulkan, Vortice.Vulkan, or any other
    /// Vulkan binding, on either side of this class. Same reasoning as <c>D3D11Com</c> gives for
    /// Core.ANGLE: different host engines pin different Vulkan binding versions (Stride uses
    /// Vortice.Vulkan internally), and a hard PackageReference here would force NuGet's resolver to
    /// pick one version for every consumer. See <see cref="VulkanNative"/> for the loader resolution
    /// and proc-address plumbing this needs instead.
    /// </item>
    /// <item>
    /// <b>Queue access must be externally synchronized.</b> Per the Vulkan spec, <c>vkQueueSubmit</c>
    /// must be externally synchronized, and Skia's <c>GrVkGpu</c> submits to the exact same
    /// <c>VkQueue</c> the host engine submits its own work to. <c>Core.VK</c> cannot invent a shared
    /// lock out of nothing - the host already owns whatever lock object serializes its own submissions
    /// (e.g. Stride's internal <c>GraphicsDevice.QueueLock</c>). <see cref="InitializeFromNative"/>
    /// therefore takes an optional <c>acquireQueueLock</c> hook: a <see cref="Func{IDisposable}"/> that
    /// <see cref="BeginDraw"/>/<see cref="EndDraw"/> invoke/dispose around the one call that actually
    /// submits to the queue (<c>GRContext.Flush(submit: true, ...)</c>). Passing <c>null</c> means "no
    /// external synchronization" - fine for a single-threaded test harness or a host that guarantees
    /// no concurrent submission on its own, but the caller's responsibility either way; this library
    /// has no way to verify it.
    /// </item>
    /// <item>
    /// <b>The post-draw <c>VkImageLayout</c> cannot be read back through this SkiaSharp version.</b>
    /// See the doc comment on <see cref="EndDraw"/> - this was verified by reflecting SkiaSharp
    /// 3.119.4's actual native P/Invoke surface, not assumed from its public API docs.
    /// </item>
    /// </list>
    /// </summary>
    public sealed class VkSkiaSurfaceFactory : IDisposable
    {
        GRContext _grContext = null!;
        Func<IDisposable>? _acquireQueueLock;
        IDisposable? _queueLockHandle;

        public GRContext GRContext => _grContext;

        /// <param name="instance">The host engine's <c>VkInstance</c>.</param>
        /// <param name="physicalDevice">The host engine's <c>VkPhysicalDevice</c>.</param>
        /// <param name="device">The host engine's <c>VkDevice</c>.</param>
        /// <param name="queue">
        /// The host engine's graphics <c>VkQueue</c> - the SAME queue the host submits its own work
        /// to. See the queue-lock discussion in this class's doc comment.
        /// </param>
        /// <param name="graphicsQueueFamilyIndex">
        /// The queue family index <paramref name="queue"/> was created from.
        /// </param>
        /// <param name="apiVersion">
        /// The Vulkan API version the host's instance/device were created against (e.g. the result of
        /// <c>VK_MAKE_API_VERSION(0, 1, 3, 0)</c> for Vulkan 1.3) - passed to
        /// <see cref="SkiaSharp.GRVkBackendContext.MaxAPIVersion"/> so Skia knows which core Vulkan
        /// entry points it may assume, as opposed to ones it must resolve as extensions.
        /// </param>
        /// <param name="instanceExtensions">
        /// The instance extensions the host actually enabled when creating <paramref name="instance"/>.
        /// </param>
        /// <param name="deviceExtensions">
        /// The device extensions the host actually enabled when creating <paramref name="device"/>.
        /// </param>
        /// <param name="vkPhysicalDeviceFeatures2">
        /// Optional raw <c>VkPhysicalDeviceFeatures2*</c> describing what the host enabled (with
        /// whatever extension structs the host chained into its <c>pNext</c>). Pass
        /// <see cref="IntPtr.Zero"/> if the host does not have one readily available - Skia will
        /// re-query <c>vkGetPhysicalDeviceFeatures2</c> against <paramref name="physicalDevice"/>
        /// itself in that case, which reports what the physical device SUPPORTS, not necessarily
        /// exactly what the host ENABLED. That distinction only matters if the host deliberately
        /// under-enables a supported feature; issue #23's research found this to be a non-issue for
        /// Stride specifically, since Stride hard-<c>throw</c>s on device creation unless a feature it
        /// needs is supported, making "supported" and "enabled" equivalent there. A host that
        /// deliberately disables an otherwise-supported feature must pass its own populated
        /// <c>VkPhysicalDeviceFeatures2</c> here instead of relying on the zero-pointer fallback.
        /// </param>
        /// <param name="acquireQueueLock">
        /// Optional hook returning an <see cref="IDisposable"/> that releases whatever lock the host
        /// uses to serialize its own <c>vkQueueSubmit</c> calls against <paramref name="queue"/>. See
        /// the queue-lock discussion in this class's doc comment. Pass <c>null</c> for no external
        /// synchronization.
        /// </param>
        public void InitializeFromNative(
            IntPtr instance, IntPtr physicalDevice, IntPtr device, IntPtr queue,
            uint graphicsQueueFamilyIndex, uint apiVersion,
            string[] instanceExtensions, string[] deviceExtensions,
            IntPtr vkPhysicalDeviceFeatures2 = default,
            Func<IDisposable>? acquireQueueLock = null)
        {
            if (instance == IntPtr.Zero)
                throw new ArgumentException("Vulkan instance native pointer is null.", nameof(instance));
            if (physicalDevice == IntPtr.Zero)
                throw new ArgumentException("Vulkan physical device native pointer is null.", nameof(physicalDevice));
            if (device == IntPtr.Zero)
                throw new ArgumentException("Vulkan device native pointer is null.", nameof(device));
            if (queue == IntPtr.Zero)
                throw new ArgumentException("Vulkan queue native pointer is null.", nameof(queue));

            _acquireQueueLock = acquireQueueLock;

            SkiaSharp.GRVkGetProcedureAddressDelegate getProc = VulkanNative.GetProcedureAddress;

            // GRVkExtensions.Create queries which of the given extension names the instance/physical
            // device actually support, feeding Skia's internal capability detection (GrVkCaps) - it
            // does not itself enable anything, it just tells Skia what it's allowed to assume is
            // already enabled on the host's instance/device.
            var extensions = GRVkExtensions.Create(
                getProc, instance, physicalDevice, instanceExtensions, deviceExtensions);

            var backendContext = new GRVkBackendContext
            {
                VkInstance = instance,
                VkPhysicalDevice = physicalDevice,
                VkDevice = device,
                VkQueue = queue,
                GraphicsQueueIndex = graphicsQueueFamilyIndex,
                MaxAPIVersion = apiVersion,
                Extensions = extensions,
                VkPhysicalDeviceFeatures2 = vkPhysicalDeviceFeatures2,
                GetProcedureAddress = getProc,
            };

            _grContext = GRContext.CreateVulkan(backendContext)
                ?? throw new InvalidOperationException("GRContext.CreateVulkan failed.");
        }

        /// <summary>
        /// Wraps the host's already-created <c>VkImage</c> (and its backing memory - see
        /// <paramref name="hasHostOwnedAllocation"/>) as a <see cref="SkiaSharp.GRVkImageInfo"/>, the
        /// Vulkan analog of <c>AngleSkiaSurfaceFactory.CreateTextureState</c>. Unlike ANGLE's
        /// <c>eglCreatePbufferFromClientBuffer</c>, there is no separate "import" call - Skia treats
        /// any <c>VkImage</c> it's given a matching <see cref="SkiaSharp.GRVkImageInfo"/> for as
        /// already usable, since it operates directly against the host's device/queue rather than a
        /// context of its own.
        /// </summary>
        /// <param name="vkImage">The host-owned <c>VkImage</c> handle to wrap.</param>
        /// <param name="format">The image's <c>VkFormat</c>.</param>
        /// <param name="imageLayout">
        /// The image's CURRENT <c>VkImageLayout</c> at the moment this call is made - Skia reads this
        /// once, as an input, to know what layout to transition from for its first internal barrier.
        /// See <see cref="EndDraw"/> for why this library cannot report back what layout the image
        /// ends up in afterward.
        /// </param>
        /// <param name="imageUsageFlags">
        /// The image's <c>VkImageUsageFlags</c> the host created it with. MUST include both
        /// <c>VK_IMAGE_USAGE_TRANSFER_SRC_BIT</c> (0x1) and <c>VK_IMAGE_USAGE_TRANSFER_DST_BIT</c>
        /// (0x2), in addition to whatever the host itself needs (typically
        /// <c>VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT</c>, 0x10, for a render target). This is a real,
        /// verified requirement, not a guess: Skia's Vulkan backend
        /// (<c>check_image_info</c> in <c>GrVkGpu.cpp</c>) unconditionally rejects wrapping ANY
        /// image - texture or render target - that is missing either transfer bit, regardless of
        /// whether the host ever intends to use them, and does so by silently returning
        /// <c>false</c> deep inside <c>SKSurface.Create</c> - the SkiaSharp-level symptom is
        /// <see cref="CreateSurface"/> throwing "SKSurface.Create failed for Vulkan backend" with no
        /// indication of which check failed. <see cref="CreateTextureState"/> checks for both bits
        /// itself and throws a specific <see cref="ArgumentException"/> instead of letting a caller
        /// hit that opaque failure later in <see cref="CreateSurface"/>.
        /// </param>
        /// <param name="imageTiling">The image's <c>VkImageTiling</c> (0 = <c>VK_IMAGE_TILING_OPTIMAL</c>, 1 = <c>VK_IMAGE_TILING_LINEAR</c>).</param>
        /// <param name="sampleCount">The image's own multisample count (<c>VkSampleCountFlagBits</c> sample count, not a bitmask) - almost always 1 for a swapchain/render-target image.</param>
        /// <param name="levelCount">The image's mip level count - almost always 1 for a render target.</param>
        /// <param name="currentQueueFamily">
        /// The queue family that currently owns the image, or <c>0xFFFFFFFF</c>
        /// (<c>VK_QUEUE_FAMILY_IGNORED</c>) if the image was created with
        /// <c>VK_SHARING_MODE_CONCURRENT</c> or no queue family ownership transfer is in flight -
        /// the correct default for a single-queue host like Stride.
        /// </param>
        /// <param name="sharingMode">The image's <c>VkSharingMode</c> (0 = <c>VK_SHARING_MODE_EXCLUSIVE</c>, 1 = <c>VK_SHARING_MODE_CONCURRENT</c>).</param>
        /// <param name="hasHostOwnedAllocation">
        /// Always <c>true</c> in the shape this library supports: the host engine created and owns
        /// the <c>VkImage</c>/<c>VkDeviceMemory</c> (a swapchain image, or a texture the host
        /// allocated itself), so Skia does not need to manage the allocation - a default/zeroed
        /// <see cref="SkiaSharp.GRVkAlloc"/> is passed. Core.VK does not support handing Skia an
        /// image for it to allocate memory for itself; every consumer wraps an image the host already
        /// owns, the same as <c>AngleSkiaSurfaceFactory.CreateTextureState</c> always wraps a host-
        /// owned D3D11 texture rather than creating one.
        /// </param>
        public VkTextureState CreateTextureState(
            ulong vkImage, uint format, uint imageLayout, uint imageUsageFlags, uint imageTiling,
            uint sampleCount = 1, uint levelCount = 1, uint currentQueueFamily = 0xFFFFFFFF,
            uint sharingMode = 0, bool hasHostOwnedAllocation = true)
        {
            const uint VK_IMAGE_USAGE_TRANSFER_SRC_BIT = 0x1;
            const uint VK_IMAGE_USAGE_TRANSFER_DST_BIT = 0x2;

            if (vkImage == 0)
                throw new ArgumentException("VkImage handle is null (0).", nameof(vkImage));
            if (!hasHostOwnedAllocation)
                throw new NotSupportedException(
                    "Core.VK only supports wrapping a host-owned VkImage/VkDeviceMemory - it does " +
                    "not allocate memory for Skia to manage.");
            if ((imageUsageFlags & VK_IMAGE_USAGE_TRANSFER_SRC_BIT) == 0 ||
                (imageUsageFlags & VK_IMAGE_USAGE_TRANSFER_DST_BIT) == 0)
                throw new ArgumentException(
                    "imageUsageFlags must include both VK_IMAGE_USAGE_TRANSFER_SRC_BIT (0x1) and " +
                    "VK_IMAGE_USAGE_TRANSFER_DST_BIT (0x2) - Skia's Vulkan backend unconditionally " +
                    "requires both on any wrapped image, regardless of whether the host uses them. " +
                    "The host must create the VkImage with these usage bits in addition to its own.",
                    nameof(imageUsageFlags));

            var imageInfo = new GRVkImageInfo
            {
                Image = vkImage,
                Alloc = default, // host-owned allocation - Skia does not manage this memory, see the doc comment above.
                ImageTiling = imageTiling,
                ImageLayout = imageLayout,
                Format = format,
                ImageUsageFlags = imageUsageFlags,
                SampleCount = sampleCount,
                LevelCount = levelCount,
                CurrentQueueFamily = currentQueueFamily,
                Protected = false,
                SharingMode = sharingMode,
            };

            return new VkTextureState(vkImage, imageInfo);
        }

        /// <summary>
        /// Creates the Skia surface backing a wrapped <c>VkImage</c>, the Vulkan analog of
        /// <c>AngleSkiaSurfaceFactory.CreateSurface</c>. Calls
        /// <c>GRContext.ResetContext(GRBackendState.All)</c> first, since Vulkan state (like GL
        /// state) can be mutated by whatever the host or another library did with the shared
        /// device/queue between draws - the same role <c>AngleSkiaSurfaceFactory.CreateSurface</c>'s
        /// <c>_grContext.ResetContext()</c> call plays for ANGLE's GL-backed context.
        /// <para>
        /// Unlike the GL overload of <c>GRBackendRenderTarget</c>, the Vulkan overload has no separate
        /// "requested MSAA sample count" parameter - SkiaSharp 3.119.4 marks the 4-argument
        /// <c>(width, height, sampleCount, GRVkImageInfo)</c> constructor obsolete in favor of the
        /// 3-argument one used here, deriving the sample count entirely from
        /// <see cref="VkTextureState"/>'s own <c>GRVkImageInfo.SampleCount</c> (the wrapped image's
        /// actual sample count) instead. There is no way to ask Skia for an internal MSAA
        /// buffer distinct from the wrapped image's own sample count on this backend.
        /// </para>
        /// </summary>
        /// <param name="colorSpace">
        /// Optional Skia color-space tag for the surface - purely a Skia-side software conversion
        /// applied when Skia writes into the wrapped image, independent of the image's own
        /// <c>VkFormat</c> (still whatever <paramref name="state"/> was created with; this never
        /// touches the underlying image). Pass <c>SKColorSpace.CreateSrgbLinear()</c> to have Skia
        /// linearize every color it draws (paint colors are always given to Skia as standard sRGB
        /// 8-bit) before writing raw bytes - the host is then responsible for treating what comes out
        /// as linear-light data. Stride's adapter uses this to compensate for Stride's own hardware
        /// sRGB encode-on-write when its default Linear <c>GraphicsDevice.ColorSpace</c> pipeline
        /// composites this texture into an sRGB-formatted render target (see
        /// <c>SkiaStrideVulkanTarget</c>'s doc comment for the full mechanism and the numbers that
        /// confirm it). Pass <c>null</c> (the default) for the previous raw-passthrough behavior,
        /// where whatever bytes Skia is told to draw land unmodified - correct for any consumer whose
        /// destination is a plain (non-sRGB-formatted) render target.
        /// </param>
        public (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            VkTextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace = null)
        {
            _grContext.ResetContext(GRBackendState.All);

            var backendRT = new GRBackendRenderTarget(width, height, state.ImageInfo);

            var surface = SKSurface.Create(_grContext, backendRT, GRSurfaceOrigin.TopLeft, colorType, colorSpace)
                ?? throw new InvalidOperationException("SKSurface.Create failed for Vulkan backend.");

            return (surface, backendRT);
        }

        /// <summary>
        /// Acquires the host's queue lock (if one was wired through <c>acquireQueueLock</c> on
        /// <see cref="InitializeFromNative"/>) before any drawing happens. Paired with
        /// <see cref="EndDraw"/>.
        /// </summary>
        public void BeginDraw()
        {
            _queueLockHandle = _acquireQueueLock?.Invoke();
        }

        /// <summary>
        /// Flushes Skia's recorded Vulkan commands and submits them to the shared <c>VkQueue</c>
        /// (<c>GRContext.Flush(submit: true, synchronous: true)</c>) - the one call in this whole
        /// class that actually calls <c>vkQueueSubmit</c>, which is why it (and not, say,
        /// <see cref="CreateSurface"/>) is what <see cref="BeginDraw"/>'s queue lock brackets.
        /// <c>synchronous: true</c> blocks until the GPU finishes, matching
        /// <c>AngleSkiaSurfaceFactory.UnbindAfterDrawing</c>'s <c>glFinish()</c> call and for the same
        /// reason: the host is about to read or resume using the image and needs the GPU work to have
        /// actually landed first. A relaxed asynchronous submit is a possible future optimization,
        /// unverified there too.
        /// <para>
        /// <b>This cannot tell the caller what <c>VkImageLayout</c> the image ends up in.</b> Verified
        /// directly against SkiaSharp 3.119.4's native P/Invoke surface (<c>SkiaApi</c>), not assumed:
        /// unlike the GL path (<c>gr_backendrendertarget_get_gl_framebufferinfo</c>), there is no
        /// <c>gr_backendrendertarget_get_vk_imageinfo</c> native entry point, and no
        /// <c>GrBackendSurfaceMutableState</c> binding either (the mechanism upstream Skia actually
        /// uses for a caller to request or query a specific post-flush Vulkan layout/queue-family -
        /// present in Skia's C++ API, simply not exposed through this SkiaSharp version's C shim).
        /// <see cref="VkTextureState"/>'s <c>ImageInfo.ImageLayout</c> is therefore a one-shot input
        /// set once in <see cref="CreateTextureState"/>, not a live handle - reading it back after
        /// this call returns the same value that was passed in, not Skia's real internal state.
        /// </para>
        /// <para>
        /// Real Vulkan has no device-side "what layout is this image in" query either - layout is
        /// always application-tracked bookkeeping, never something the driver reports back. The
        /// fallback this leaves hosts with: a <c>VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT</c> image being
        /// drawn into must sit in <c>VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL</c> (or
        /// <c>VK_IMAGE_LAYOUT_GENERAL</c>) while Skia's draw commands execute, per the Vulkan spec,
        /// and nothing in this flush path transitions it anywhere else afterward - so
        /// <c>COLOR_ATTACHMENT_OPTIMAL</c> is the ASSUMED post-<see cref="EndDraw"/> layout for such
        /// an image, not a value this library can verify or guarantee. A host needing certainty must
        /// insert its own <c>vkCmdPipelineBarrier</c> (with whatever queue-ownership transfer it
        /// needs) rather than trust a reported value, since none exists.
        /// </para>
        /// </summary>
        public void EndDraw()
        {
            try
            {
                _grContext.Flush(true, true);
            }
            finally
            {
                _queueLockHandle?.Dispose();
                _queueLockHandle = null;
            }
        }

        public void Dispose()
        {
            _queueLockHandle?.Dispose();
            _queueLockHandle = null;
            _grContext?.Dispose();
        }
    }
}
