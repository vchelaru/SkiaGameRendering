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
    /// <para>
    /// <b>multiSampleCount.</b> With 2, 4 or 8, Skia draws into a second, multisampled image this object owns, and
    /// <see cref="End"/> resolves it into <see cref="VkImage"/> with <c>vkCmdResolveImage</c>, in the submit that
    /// otherwise moved the layout. <see cref="VkImage"/> stays single-sample and the layout contract above is unchanged;
    /// the app never sees the multisampled image. <see cref="Begin"/> no longer touches <see cref="VkImage"/>, so it
    /// submits nothing.
    /// </para>
    /// </summary>
    public sealed class SkiaRenderTarget2D : IDisposable
    {
        const uint FormatR8G8B8A8Unorm = 37;
        const uint FormatB8G8R8A8Unorm = 44;

        readonly SkiaVulkanContext _context;
        VkImageAllocation _allocation;
        // The multisampled image Skia draws into when multiSampleCount is above 0; default otherwise.
        VkImageAllocation _multiSampled;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        readonly bool _appManagesLayout;
        bool _hasBegun;
        bool _disposed;

        /// <param name="multiSampleCount">
        /// Samples per pixel. 0 (the default) is a plain surface; 2, 4 and 8 are the GPU's built-in edge smoothing. Any
        /// other value, or a count the GPU lacks for <paramref name="colorType"/>, throws <see cref="NotSupportedException"/>;
        /// it never falls back to 0.
        /// </param>
        public SkiaRenderTarget2D(SkiaVulkanContext context, int width, int height, SKColorType colorType = SKColorType.Rgba8888,
            bool appManagesLayout = false, int multiSampleCount = 0)
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
            if (multiSampleCount is not (0 or 2 or 4 or 8))
                throw new NotSupportedException($"multiSampleCount must be 0, 2, 4 or 8, not {multiSampleCount}.");

            if (multiSampleCount > 0 && multiSampleCount > context.Factory.GetMaxSurfaceSampleCount(colorType))
                throw new NotSupportedException(
                    $"multiSampleCount {multiSampleCount} is not supported: this GPU offers at most " +
                    $"{context.Factory.GetMaxSurfaceSampleCount(colorType)} samples for {colorType}.");

            _context = context;
            _appManagesLayout = appManagesLayout;
            Width = width;
            Height = height;
            _allocation = VkImageAllocator.Create(context.Instance, context.PhysicalDevice, context.Device, width, height, format);
            try
            {
                // Hand the image to the app's layout contract first: UNDEFINED -> SHADER_READ_ONLY.
                Transition(_allocation.Image, VkConstants.ImageLayoutUndefined,
                    _appManagesLayout ? VkConstants.ImageLayoutColorAttachmentOptimal : VkConstants.ImageLayoutShaderReadOnlyOptimal);

                // Skia draws into the multisampled image when there is one; End resolves it into the app's.
                var skiaImage = _allocation;
                if (multiSampleCount > 0)
                {
                    _multiSampled = VkImageAllocator.Create(context.Instance, context.PhysicalDevice, context.Device,
                        width, height, format, samples: (uint)multiSampleCount);
                    skiaImage = _multiSampled;
                    Transition(_multiSampled.Image, VkConstants.ImageLayoutUndefined, VkConstants.ImageLayoutColorAttachmentOptimal);
                }

                // Skia is told COLOR_ATTACHMENT_OPTIMAL, which is where Begin() puts the image before it draws.
                var state = context.Factory.CreateTextureState(
                    skiaImage.Image, format, VkConstants.ImageLayoutColorAttachmentOptimal,
                    skiaImage.UsageFlags, imageTiling: 0, sampleCount: multiSampleCount > 0 ? (uint)multiSampleCount : 1);
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

            if (!_appManagesLayout && _multiSampled.Image == 0)
                Transition(_allocation.Image, VkConstants.ImageLayoutShaderReadOnlyOptimal, VkConstants.ImageLayoutColorAttachmentOptimal);
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
            if (_multiSampled.Image != 0)
                Resolve();
            else if (!_appManagesLayout)
                Transition(_allocation.Image, VkConstants.ImageLayoutColorAttachmentOptimal, VkConstants.ImageLayoutShaderReadOnlyOptimal);
            if (synchronous)
                _context.Transitioner.WaitForCompletion();
        }

        // The app's image sits in its contract layout (SHADER_READ_ONLY, or COLOR_ATTACHMENT when the app manages
        // layout) and comes back to it, its last and next use being a fragment-shader read or the app's barrier.
        void Resolve()
        {
            var layout = _appManagesLayout ? VkConstants.ImageLayoutColorAttachmentOptimal : VkConstants.ImageLayoutShaderReadOnlyOptimal;
            _context.Transitioner.Resolve(
                _multiSampled.Image, _allocation.Image, (uint)Width, (uint)Height,
                destinationLayout: layout,
                destinationSrcStageMask: _appManagesLayout ? VkConstants.PipelineStageColorAttachmentOutput : VkConstants.PipelineStageFragmentShader,
                destinationFinalLayout: layout,
                destinationDstStageMask: _appManagesLayout ? VkConstants.PipelineStageColorAttachmentOutput : VkConstants.PipelineStageFragmentShader,
                destinationDstAccessMask: _appManagesLayout
                    ? VkConstants.AccessColorAttachmentRead | VkConstants.AccessColorAttachmentWrite
                    : VkConstants.AccessShaderRead);
        }

        void Transition(ulong image, uint oldLayout, uint newLayout)
        {
            // Skia's last writes are color-attachment writes; the app's first read is a fragment-shader read.
            var toShaderRead = newLayout == VkConstants.ImageLayoutShaderReadOnlyOptimal;
            var fromUndefined = oldLayout == VkConstants.ImageLayoutUndefined;
            _context.Transitioner.Transition(
                image, oldLayout, newLayout,
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
            if (_allocation.Image != 0 || _multiSampled.Image != 0)
            {
                _context.Transitioner.WaitForCompletion();
                VkImageAllocator.Destroy(_context.Device, _allocation);
                VkImageAllocator.Destroy(_context.Device, _multiSampled);
                _allocation = default;
                _multiSampled = default;
            }
        }
    }
}
