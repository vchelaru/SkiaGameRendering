using SkiaGameRendering.Core.VK;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// A Vulkan image that SkiaSharp renders into on the GPU. Same shape as <c>SpriteBatch</c> and the other
    /// engines' <c>SkiaRenderTarget2D</c>:
    /// <code>
    /// target.Begin();
    /// target.Canvas.DrawCircle(100, 100, 100, paint);
    /// target.End();
    /// // sample target.VkImage in your own pass; no barrier needed
    /// </code>
    /// <para>
    /// <b>Contract.</b> The image is <c>VK_FORMAT_R8G8B8A8_UNORM</c> or <c>B8G8R8A8_UNORM</c> (per
    /// <see cref="SKColorType"/>), single-sample, with usage TRANSFER_SRC | TRANSFER_DST | SAMPLED |
    /// COLOR_ATTACHMENT, on the context's queue family. It is in <c>SHADER_READ_ONLY_OPTIMAL</c> whenever
    /// the app owns it (before <see cref="Begin"/> and after <see cref="End"/>), so the app can sample it
    /// right away. <see cref="End"/> has submitted Skia's work to the context's queue; a consumer on that
    /// same queue is ordered behind it, with the writes made visible to fragment-shader reads. A consumer
    /// on another queue needs <c>End(synchronous: true)</c> plus its own ownership handling. The app
    /// creates its own <c>VkImageView</c>.
    /// </para>
    /// <para>
    /// <b>appManagesLayout.</b> With <c>appManagesLayout: true</c> the library records no transitions: the image
    /// stays in <c>COLOR_ATTACHMENT_OPTIMAL</c> between Skia passes, and the app records, in its own command
    /// buffer around its sampling, COLOR_ATTACHMENT_OPTIMAL to SHADER_READ_ONLY_OPTIMAL before the read and back
    /// after it (src COLOR_ATTACHMENT_OUTPUT / COLOR_ATTACHMENT_WRITE to dst FRAGMENT_SHADER / SHADER_READ, and the
    /// reverse). That removes the library's two small submits per frame. Disposing needs the GPU to be finished with the image.
    /// </para>
    /// </summary>
    public sealed class SkiaRenderTarget2D : IDisposable
    {
        const uint FormatR8G8B8A8Unorm = 37;
        const uint FormatB8G8R8A8Unorm = 44;

        readonly SkiaVulkanContext _context;
        VkImageAllocation _allocation;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        readonly bool _appManagesLayout;
        bool _hasBegun;
        bool _disposed;

        public SkiaRenderTarget2D(SkiaVulkanContext context, int width, int height, SKColorType colorType = SKColorType.Rgba8888, bool appManagesLayout = false)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            var format = colorType switch
            {
                SKColorType.Rgba8888 => FormatR8G8B8A8Unorm,
                SKColorType.Bgra8888 => FormatB8G8R8A8Unorm,
                _ => throw new NotSupportedException($"SkiaRenderTarget2D on BRUTAL draws Rgba8888 or Bgra8888, not {colorType}."),
            };

            _context = context;
            _appManagesLayout = appManagesLayout;
            Width = width;
            Height = height;
            _allocation = VkImageAllocator.Create(context.Instance, context.PhysicalDevice, context.Device, width, height, format);
            try
            {
                // Hand the image to the app's layout contract first: UNDEFINED -> SHADER_READ_ONLY.
                Transition(VkConstants.ImageLayoutUndefined,
                    _appManagesLayout ? VkConstants.ImageLayoutColorAttachmentOptimal : VkConstants.ImageLayoutShaderReadOnlyOptimal);

                // Skia is told COLOR_ATTACHMENT_OPTIMAL, which is where Begin() puts the image before it draws.
                var state = context.Factory.CreateTextureState(
                    _allocation.Image, format, VkConstants.ImageLayoutColorAttachmentOptimal,
                    _allocation.UsageFlags, imageTiling: 0);
                context.Factory.BeginDraw();
                try
                {
                    (_surface, _renderTarget) = context.Factory.CreateSurface(state, width, height, colorType);
                }
                finally
                {
                    context.Factory.EndDraw(synchronous: false);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>The raw <c>VkImage</c> handle, valid until <see cref="Dispose"/>.</summary>
        public ulong VkImage => _allocation.Image;

        /// <summary>The raw <c>VkFormat</c> value of <see cref="VkImage"/>.</summary>
        public uint VkFormat => _allocation.Format;

        /// <summary>The canvas to draw on. Only valid between <see cref="Begin"/> and <see cref="End"/>.</summary>
        public SkiaSharp.SKCanvas Canvas => _hasBegun
            ? _surface!.Canvas
            : throw new InvalidOperationException("Begin must be called before accessing Canvas.");

        /// <summary>
        /// Takes the image from the app (SHADER_READ_ONLY_OPTIMAL to COLOR_ATTACHMENT_OPTIMAL, queued on the
        /// context's queue) and starts a Skia pass. <paramref name="clear"/> clears to transparent; pass
        /// <c>false</c> to draw over the previous contents.
        /// </summary>
        public void Begin(bool clear = true)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hasBegun)
                throw new InvalidOperationException("Begin cannot be called again until End has been called.");

            if (!_appManagesLayout)
                Transition(VkConstants.ImageLayoutShaderReadOnlyOptimal, VkConstants.ImageLayoutColorAttachmentOptimal);
            _context.Factory.BeginDraw();
            _hasBegun = true;
            if (clear)
                _surface!.Canvas.Clear();
        }

        /// <summary>
        /// Flushes Skia's work to the context's queue, then queues the move to SHADER_READ_ONLY_OPTIMAL
        /// behind it. Returns without waiting for the GPU unless <paramref name="synchronous"/> is true.
        /// </summary>
        public void End(bool synchronous = false)
        {
            if (!_hasBegun)
                throw new InvalidOperationException("Begin must be called before calling End.");

            try
            {
                _surface!.Flush();
            }
            finally
            {
                _context.Factory.EndDraw(synchronous);
                _hasBegun = false;
            }
            if (!_appManagesLayout)
                Transition(VkConstants.ImageLayoutColorAttachmentOptimal, VkConstants.ImageLayoutShaderReadOnlyOptimal);
            if (synchronous)
                _context.Transitioner.WaitForCompletion();
        }

        void Transition(uint oldLayout, uint newLayout)
        {
            // Skia's last writes are color-attachment writes; the app's first read is a fragment-shader read.
            var toShaderRead = newLayout == VkConstants.ImageLayoutShaderReadOnlyOptimal;
            var fromUndefined = oldLayout == VkConstants.ImageLayoutUndefined;
            _context.Transitioner.Transition(
                _allocation.Image, oldLayout, newLayout,
                srcStageMask: fromUndefined ? VkConstants.PipelineStageTopOfPipe
                    : VkConstants.PipelineStageFragmentShader | VkConstants.PipelineStageColorAttachmentOutput,
                srcAccessMask: toShaderRead && !fromUndefined ? VkConstants.AccessColorAttachmentWrite : 0u,
                dstStageMask: toShaderRead ? VkConstants.PipelineStageFragmentShader : VkConstants.PipelineStageColorAttachmentOutput,
                dstAccessMask: toShaderRead ? VkConstants.AccessShaderRead : VkConstants.AccessColorAttachmentWrite);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            if (_hasBegun)
                throw new InvalidOperationException("Dispose cannot be called between Begin and End; call End first.");
            _disposed = true;

            _surface?.Dispose();
            _renderTarget?.Dispose();
            _surface = null;
            _renderTarget = null;
            if (_allocation.Image != 0)
            {
                _context.Transitioner.WaitForCompletion();
                VkImageAllocator.Destroy(_context.Device, _allocation);
                _allocation = default;
            }
        }
    }
}
