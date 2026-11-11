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
    /// per pass, because a swapchain hands out a different image each frame. Use it once per frame, not once
    /// per widget.
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

        readonly SkiaVulkanContext _context;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        ulong _image;
        uint _cameInLayout;
        bool _hasBegun;
        bool _disposed;

        public SkiaDirectCanvas(SkiaVulkanContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            _context = context;
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
            var fromUndefined = currentLayout == VkConstants.ImageLayoutUndefined;
            _context.Transitioner.Transition(
                vkImage, currentLayout, VkConstants.ImageLayoutColorAttachmentOptimal,
                srcStageMask: fromUndefined ? VkConstants.PipelineStageTopOfPipe : PipelineStageAllCommands,
                srcAccessMask: fromUndefined ? 0u : AccessMemoryWrite,
                dstStageMask: VkConstants.PipelineStageColorAttachmentOutput,
                dstAccessMask: AccessColorAttachmentRead | VkConstants.AccessColorAttachmentWrite);

            _context.Factory.BeginDraw();
            try
            {
                var state = _context.Factory.CreateTextureState(
                    vkImage, unormFormat, VkConstants.ImageLayoutColorAttachmentOptimal,
                    imageUsage | VkConstants.ImageUsageTransferSrc | VkConstants.ImageUsageTransferDst, imageTiling: 0);
                (_surface, _renderTarget) = _context.Factory.CreateSurface(state, width, height, colorType);
            }
            catch
            {
                _context.Factory.EndDraw(synchronous: false);
                // Hand the image back where it came from.
                MoveTo(vkImage, currentLayout == VkConstants.ImageLayoutUndefined ? VkConstants.ImageLayoutColorAttachmentOptimal : currentLayout);
                throw;
            }

            _image = vkImage;
            _cameInLayout = currentLayout;
            _hasBegun = true;
            if (clear)
                _surface.Canvas.Clear();
        }

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
                _surface!.Flush();
            }
            finally
            {
                try
                {
                    _context.Factory.EndDraw(synchronous);
                }
                finally
                {
                    _surface!.Dispose();
                    _renderTarget!.Dispose();
                    _surface = null;
                    _renderTarget = null;
                    _hasBegun = false;
                }
            }
            MoveTo(_image, layout);
            if (synchronous)
                _context.Transitioner.WaitForCompletion();
        }

        // COLOR_ATTACHMENT_OPTIMAL to the app's layout: Skia's color writes become visible to everything after.
        void MoveTo(ulong image, uint layout)
        {
            var present = layout == ImageLayoutPresentSrcKhr;
            _context.Transitioner.Transition(
                image, VkConstants.ImageLayoutColorAttachmentOptimal, layout,
                srcStageMask: VkConstants.PipelineStageColorAttachmentOutput,
                srcAccessMask: VkConstants.AccessColorAttachmentWrite,
                dstStageMask: present ? VkConstants.PipelineStageBottomOfPipe : PipelineStageAllCommands,
                dstAccessMask: present ? 0u : AccessMemoryRead | AccessMemoryWrite);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            if (_hasBegun)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");
            _disposed = true;
        }
    }
}
