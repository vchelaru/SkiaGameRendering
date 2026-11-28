using SkiaGameRendering.Core.Metal;
using SkiaGameRendering.Core.VK;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// Draws with SkiaSharp straight into a Vulkan image the app supplies, typically the swapchain image it is
    /// about to present, with no intermediate image. Same shape as <see cref="SkiaRenderTarget2D"/>. Vulkan
    /// has no bound-target query and BRUTAL has no renderer type, so <see cref="Begin"/> takes the target
    /// explicitly:
    /// <code>
    /// var direct = new SkiaDirectCanvas(context);
    /// // after submitting your earlier work on the context's queue:
    /// direct.Begin(swapchainImage, swapchainFormat, width, height, currentLayout);
    /// direct.Canvas.DrawCircle(100, 100, 50, paint);
    /// direct.End(finalLayout: PresentSrcKhr);
    /// </code>
    /// <see cref="Begin"/> does not clear unless asked; Skia draws on top of what is there. Coordinates are
    /// image pixels with the origin at the top left.
    /// <para>
    /// <b>Contract.</b> The image is 8-bit RGBA or BGRA (UNORM, or the sRGB twin, see below), single-sample, on
    /// the context's queue family, created with <c>COLOR_ATTACHMENT</c> usage. Skia insists on seeing
    /// <c>TRANSFER_SRC | TRANSFER_DST</c> on any image it wraps and is told the image has them; drawing
    /// into it uses neither. <see cref="Begin"/> moves the image from the layout the app says it is in to
    /// <c>COLOR_ATTACHMENT_OPTIMAL</c> and <see cref="End"/> moves it to the layout the app names, or back to the
    /// one it came in, each as a small submit on the context's queue. Both barriers cover all earlier commands
    /// on that queue, so the app's earlier writes are ordered before Skia's.
    /// </para>
    /// <para>
    /// <b>Submit order.</b> Skia submits its work to the queue inside <see cref="End"/>, not into a command buffer
    /// the app owns. Submit everything that draws into the image before <see cref="Begin"/>, and everything that
    /// reads it (including present) after <see cref="End"/>. Use <c>End(synchronous: true)</c> when the next
    /// consumer is on another queue or the CPU.
    /// </para>
    /// <para>
    /// <b>sRGB.</b> Skia cannot wrap an sRGB <c>VkImage</c>, so an sRGB image is wrapped as its UNORM twin (same
    /// texel layout) and Skia writes the bytes as given: an <see cref="SKColor"/> lands as that color. Without
    /// <c>MUTABLE_FORMAT</c> on the image, the validation layer flags this; drivers accept it for same-size formats.
    /// </para>
    /// <para>
    /// <b>Cost.</b> Three queue submits per pass (the two layout moves and Skia's), and a Skia surface wrapped
    /// per pass unless <c>cacheSurfaces</c> is on. Use it once per frame, not once per widget.
    /// </para>
    /// <para>
    /// <b>Surface cache.</b> <c>new SkiaDirectCanvas(context, cacheSurfaces: true)</c> keeps the Skia surface it wraps
    /// around each image (keyed on image handle, format and size) instead of making a new one every pass, so a swapchain's
    /// two or three images are wrapped once each. When you recreate the swapchain, call <see cref="ReleaseCachedSurfaces"/>
    /// where you destroy your own views of its images: Vulkan can give a new image the handle of a destroyed one, and
    /// the cache cannot tell them apart. On macOS nothing is cached, since the texture changes with each drawable.
    /// </para>
    /// <para>
    /// <b>macOS (MoltenVK).</b> Skia draws with Metal into the <c>MTLTexture</c> behind the image, exported with
    /// <c>VK_EXT_metal_objects</c> at every <see cref="Begin"/> (a swapchain image's texture is the current
    /// <c>CAMetalDrawable</c>'s, so call <see cref="Begin"/> after <c>vkAcquireNextImageKHR</c>). The layout moves and
    /// submit order above still hold; with MoltenVK's asynchronous submits Skia draws on its own <c>MTLCommandQueue</c>,
    /// and the two layout moves also carry the timeline semaphore waits that order it against the context's queue
    /// (see <see cref="SkiaVulkanContext"/>). Two extra rules. The swapchain needs <c>TRANSFER_SRC</c>, <c>SAMPLED</c> or
    /// <c>STORAGE</c> in its <c>imageUsage</c>, or MoltenVK makes its textures <c>framebufferOnly</c>, which Skia's
    /// Metal backend cannot draw every blend mode into, and <see cref="Begin"/> throws. And exporting a swapchain image's
    /// texture breaks the Vulkan spec: the image wasn't created with <c>VkExportMetalObjectCreateInfoEXT</c>, which a
    /// swapchain image can't be, so the validation layer reports VUID-VkExportMetalObjectsInfoEXT-pNext-06795. MoltenVK
    /// exports it anyway. An image you create yourself with that struct chained breaks nothing.
    /// </para>
    /// </summary>
    public sealed class SkiaDirectCanvas : IDisposable
    {
        const uint FormatR8G8B8A8Unorm = 37;
        const uint FormatR8G8B8A8Srgb = 43;
        const uint FormatB8G8R8A8Unorm = 44;
        const uint FormatB8G8R8A8Srgb = 50;
        const uint ImageLayoutPreinitialized = 8;
        const uint ImageLayoutPresentSrcKhr = 1000001002;
        const uint PipelineStageAllCommands = 0x10000;
        const uint AccessColorAttachmentRead = 0x80;
        const uint AccessMemoryRead = 0x8000;
        const uint AccessMemoryWrite = 0x10000;

        /// <summary><c>VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT</c> | <c>TRANSFER_SRC</c> | <c>TRANSFER_DST</c>, what Skia needs to see on a wrapped image.</summary>
        public const uint DefaultImageUsage =
            VkConstants.ImageUsageTransferSrc | VkConstants.ImageUsageTransferDst | VkConstants.ImageUsageColorAttachment;

        const int MaxCachedSurfaces = 8;

        readonly SkiaVulkanContext _context;
        readonly Dictionary<(ulong image, uint format, int width, int height), (SKSurface surface, GRBackendRenderTarget renderTarget)>? _cache;
        int _saveCount;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        MetalDirectSurface? _metalSurface;
        ulong _image;
        uint _cameInLayout;
        bool _hasBegun;
        bool _disposed;

        /// <param name="context">The Vulkan context Skia draws with.</param>
        /// <param name="cacheSurfaces">Keep one Skia surface per image across passes; see the class remarks for the rule it adds.</param>
        public SkiaDirectCanvas(SkiaVulkanContext context, bool cacheSurfaces = false)
        {
            ArgumentNullException.ThrowIfNull(context);
            _context = context;
            if (cacheSurfaces)
                _cache = [];
        }

        /// <summary>
        /// The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>;
        /// accessing it outside that window throws.
        /// </summary>
        public SKCanvas Canvas => _hasBegun
            ? _surface!.Canvas
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Starts a pass into <paramref name="vkImage"/>. <paramref name="currentLayout"/> is the layout the image is
        /// in now (<c>UNDEFINED</c> if its contents don't matter, <c>PRESENT_SRC_KHR</c> for a swapchain image that
        /// was presented before). Throws <see cref="NotSupportedException"/> for a multisampled image or a format
        /// other than 8-bit RGBA or BGRA, and <see cref="ArgumentException"/> for a null image or an
        /// <paramref name="imageUsage"/> without <c>COLOR_ATTACHMENT</c>. A refused <c>Begin</c> changes nothing.
        /// </summary>
        /// <param name="vkImage">The raw <c>VkImage</c> handle.</param>
        /// <param name="vkFormat">The image's <c>VkFormat</c>.</param>
        /// <param name="width">The image's width in pixels.</param>
        /// <param name="height">The image's height in pixels.</param>
        /// <param name="currentLayout">The image's current <c>VkImageLayout</c>.</param>
        /// <param name="clear">Clear to transparent first; the default draws over what is there.</param>
        /// <param name="imageUsage">The <c>VkImageUsageFlags</c> the image was created with, checked for <c>COLOR_ATTACHMENT</c>.</param>
        /// <param name="sampleCount">The image's sample count; anything but 1 throws.</param>
        public void Begin(
            ulong vkImage, uint vkFormat, int width, int height, uint currentLayout,
            bool clear = false, uint imageUsage = DefaultImageUsage, uint sampleCount = 1)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");
            if (vkImage == 0)
                throw new ArgumentException("VkImage handle is null (0).", nameof(vkImage));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if (sampleCount != 1)
                throw new NotSupportedException($"SkiaDirectCanvas needs a single-sample image, not {sampleCount} samples.");
            if ((imageUsage & VkConstants.ImageUsageColorAttachment) == 0)
                throw new ArgumentException("The image must be created with COLOR_ATTACHMENT usage for Skia to draw into it.", nameof(imageUsage));
            if (currentLayout == ImageLayoutPreinitialized)
                throw new ArgumentException("PREINITIALIZED is not a layout an image can be moved out of here; use UNDEFINED.", nameof(currentLayout));
            var (colorType, unormFormat) = vkFormat switch
            {
                FormatR8G8B8A8Unorm or FormatR8G8B8A8Srgb => (SKColorType.Rgba8888, FormatR8G8B8A8Unorm),
                FormatB8G8R8A8Unorm or FormatB8G8R8A8Srgb => (SKColorType.Bgra8888, FormatB8G8R8A8Unorm),
                _ => throw new NotSupportedException($"SkiaDirectCanvas draws into 8-bit RGBA or BGRA images, not VkFormat {vkFormat}."),
            };

            // Any earlier command on the queue may have written the image; the move makes those writes visible to Skia's.
            // On macOS with asynchronous submits the same submit signals the value Skia's Metal work waits for.
            var metalOrderValue = _context.OrdersMetalWithSemaphore ? _context.NextMetalOrderValue() : 0;
            var fromUndefined = currentLayout == VkConstants.ImageLayoutUndefined;
            _context.Transitioner.Transition(
                vkImage, currentLayout, VkConstants.ImageLayoutColorAttachmentOptimal,
                srcStageMask: fromUndefined ? VkConstants.PipelineStageTopOfPipe : PipelineStageAllCommands,
                srcAccessMask: fromUndefined ? 0u : AccessMemoryWrite,
                dstStageMask: VkConstants.PipelineStageColorAttachmentOutput,
                dstAccessMask: AccessColorAttachmentRead | VkConstants.AccessColorAttachmentWrite,
                timelineSemaphore: metalOrderValue != 0 ? _context.MetalOrderSemaphore : 0, signalValue: metalOrderValue);

            if (_context.MetalFactory is { } metal)
            {
                if (metalOrderValue != 0)
                    _context.CommitMetalWait(metalOrderValue);
                BeginMetal(metal, vkImage, width, height, currentLayout);
            }
            else
            {
                _context.Factory.BeginDraw();
                try
                {
                    var key = (vkImage, unormFormat, width, height);
                    if (_cache != null && _cache.TryGetValue(key, out var cached))
                        (_surface, _renderTarget) = cached;
                    else
                    {
                        var state = _context.Factory.CreateTextureState(
                            vkImage, unormFormat, VkConstants.ImageLayoutColorAttachmentOptimal,
                            imageUsage | VkConstants.ImageUsageTransferSrc | VkConstants.ImageUsageTransferDst, imageTiling: 0);
                        (_surface, _renderTarget) = _context.Factory.CreateSurface(state, width, height, colorType);
                        if (_cache != null)
                        {
                            // A swapchain has two or three images; more entries means it was recreated without a release.
                            if (_cache.Count >= MaxCachedSurfaces)
                                ReleaseCache();
                            _cache[key] = (_surface, _renderTarget);
                        }
                    }
                }
                catch
                {
                    _context.Factory.EndDraw(synchronous: false);
                    HandBack(vkImage, currentLayout);
                    throw;
                }
            }

            _image = vkImage;
            _cameInLayout = currentLayout;
            _hasBegun = true;
            // A cached surface keeps its canvas; End restores to here so each pass starts with no matrix or clip, like a new one.
            _saveCount = _surface!.Canvas.Save();
            if (clear)
                _surface!.Canvas.Clear();
        }

        // macOS: the texture is exported every pass, since a swapchain image's texture changes with each acquired drawable.
        void BeginMetal(MetalSkiaSurfaceFactory metal, ulong vkImage, int width, int height, uint currentLayout)
        {
            try
            {
                var texture = VkMetalObjectExport.ExportTexture(_context.Device, vkImage);
                if (MetalSkiaSurfaceFactory.IsFramebufferOnly(texture))
                    throw new NotSupportedException(
                        "The image's MTLTexture is framebufferOnly, which MoltenVK makes a swapchain's textures unless its imageUsage " +
                        "includes TRANSFER_SRC, SAMPLED or STORAGE. Skia's Metal backend copies the destination for some blend modes, " +
                        "which Metal forbids on such a texture. Add VK_IMAGE_USAGE_TRANSFER_SRC_BIT to the swapchain's imageUsage.");
                metal.BeginDraw();
                _metalSurface = metal.CreateDirectSurface(texture, width, height);
                _surface = _metalSurface.Surface;
            }
            catch
            {
                HandBack(vkImage, currentLayout);
                throw;
            }
        }

        // A refused Begin hands the image back where it came from.
        void HandBack(ulong image, uint currentLayout) =>
            MoveTo(image, currentLayout == VkConstants.ImageLayoutUndefined ? VkConstants.ImageLayoutColorAttachmentOptimal : currentLayout);

        /// <summary>
        /// Flushes Skia's work to the context's queue, then queues the move to <paramref name="finalLayout"/> (default:
        /// the layout the image came in with, or <c>COLOR_ATTACHMENT_OPTIMAL</c> if it came in <c>UNDEFINED</c>)
        /// behind it. Returns without waiting for the GPU unless <paramref name="synchronous"/> is true.
        /// </summary>
        public void End(uint? finalLayout = null, bool synchronous = false)
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before calling End.");
            var layout = finalLayout ?? (_cameInLayout == VkConstants.ImageLayoutUndefined ? VkConstants.ImageLayoutColorAttachmentOptimal : _cameInLayout);
            if (layout == VkConstants.ImageLayoutUndefined || layout == ImageLayoutPreinitialized)
                throw new ArgumentException("An image cannot be handed back as UNDEFINED or PREINITIALIZED.", nameof(finalLayout));

            try
            {
                _surface!.Canvas.RestoreToCount(_saveCount);
                // On Vulkan, Factory.EndDraw makes this same flush under the queue lock.
                if (_context.MetalFactory != null)
                    _surface.Flush();
            }
            finally
            {
                try
                {
                    // macOS with the semaphore: synchronous waits on the move below, which runs after Skia's work.
                    if (_context.MetalFactory is { } metal)
                        metal.EndDraw(synchronous && !_context.OrdersMetalWithSemaphore);
                    else
                        _context.Factory.EndDraw(synchronous);
                }
                finally
                {
                    if (_metalSurface != null)
                        _metalSurface.Dispose();
                    else if (_cache == null)
                    {
                        _surface!.Dispose();
                        _renderTarget!.Dispose();
                    }
                    _metalSurface = null;
                    _surface = null;
                    _renderTarget = null;
                    _hasBegun = false;
                }
            }
            // On macOS with asynchronous submits the move waits for Skia's Metal work, and so does everything the app
            // submits after it.
            var metalOrderValue = 0ul;
            if (_context.OrdersMetalWithSemaphore)
            {
                metalOrderValue = _context.NextMetalOrderValue();
                _context.CommitMetalSignal(metalOrderValue);
            }
            MoveTo(_image, layout, metalOrderValue);
            if (synchronous)
                _context.Transitioner.WaitForCompletion();
        }

        // COLOR_ATTACHMENT_OPTIMAL to the app's layout: Skia's color writes become visible to everything after.
        void MoveTo(ulong image, uint layout, ulong metalOrderWait = 0)
        {
            var present = layout == ImageLayoutPresentSrcKhr;
            _context.Transitioner.Transition(
                image, VkConstants.ImageLayoutColorAttachmentOptimal, layout,
                srcStageMask: VkConstants.PipelineStageColorAttachmentOutput,
                srcAccessMask: VkConstants.AccessColorAttachmentWrite,
                dstStageMask: present ? VkConstants.PipelineStageBottomOfPipe : PipelineStageAllCommands,
                dstAccessMask: present ? 0u : AccessMemoryRead | AccessMemoryWrite,
                timelineSemaphore: metalOrderWait != 0 ? _context.MetalOrderSemaphore : 0, waitValue: metalOrderWait);
        }

        /// <summary>
        /// Disposes every cached surface. Call it when the swapchain is recreated, before the next <see cref="Begin"/>; a
        /// later pass into any image wraps it again. Does nothing when <c>cacheSurfaces</c> is off.
        /// </summary>
        public void ReleaseCachedSurfaces()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hasBegun)
                throw new InvalidOperationException("ReleaseCachedSurfaces cannot be called between Begin and End; call End first.");
            ReleaseCache();
        }

        void ReleaseCache()
        {
            if (_cache == null)
                return;
            foreach (var (surface, renderTarget) in _cache.Values)
            {
                surface.Dispose();
                renderTarget.Dispose();
            }
            _cache.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            if (_hasBegun)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");
            ReleaseCache();
            _disposed = true;
        }
    }
}
