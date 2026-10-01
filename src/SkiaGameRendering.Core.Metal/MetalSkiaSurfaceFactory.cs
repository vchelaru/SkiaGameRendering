using SkiaSharp;

namespace SkiaGameRendering.Core.Metal
{
    /// <summary>
    /// A single host-supplied <c>id&lt;MTLTexture&gt;</c> wrapped for Skia. Returned by
    /// <see cref="MetalSkiaSurfaceFactory.CreateTextureState"/> and consumed by
    /// <see cref="MetalSkiaSurfaceFactory.CreateSurface"/>.
    /// </summary>
    public sealed class MetalTextureState
    {
        internal MetalTextureState(IntPtr texture, GRMtlTextureInfo textureInfo)
        {
            Texture = texture;
            TextureInfo = textureInfo;
        }

        /// <summary>The wrapped <c>id&lt;MTLTexture&gt;</c>, for the host's own bookkeeping.</summary>
        public IntPtr Texture { get; }

        internal GRMtlTextureInfo TextureInfo { get; }
    }

    /// <summary>
    /// Engine-agnostic Metal/Skia interop, the Metal member of the <c>Core.VK</c>/<c>Core.D3D12</c>
    /// family. Like them it creates nothing of its own: the host's <c>id&lt;MTLDevice&gt;</c> and
    /// <c>id&lt;MTLCommandQueue&gt;</c> go straight to Skia's <c>GrMtlGpu</c> through
    /// <see cref="GRMtlBackendContext"/>, and Skia's command buffers are committed to that same queue.
    ///
    /// MAINTENANCE NOTES:
    /// <list type="bullet">
    /// <item>
    /// <b>No queue lock, unlike Core.VK and Core.D3D12.</b> <c>MTLCommandQueue</c> is documented
    /// thread-safe, so a host committing its own command buffers from another thread needs no
    /// external synchronization against Skia's.
    /// </item>
    /// <item>
    /// <b>No layout or state transitions.</b> Metal textures have neither, so there is no
    /// <c>VkImageLayoutTransitioner</c>/<c>D3D12ResourceTransitioner</c> equivalent. Ordering between
    /// Skia's writes and the host's reads comes from Metal's automatic hazard tracking and command
    /// buffer commit order on the shared queue. A host that creates its textures with
    /// <c>MTLHazardTrackingModeUntracked</c> gets no such ordering and has to synchronize itself.
    /// </item>
    /// <item>
    /// <b>The texture's format comes from the texture.</b> <see cref="GRMtlTextureInfo"/> carries only
    /// the pointer; Skia reads <c>pixelFormat</c> off the <c>MTLTexture</c>, so the
    /// <see cref="SKColorType"/> passed to <see cref="CreateSurface"/> must match it
    /// (<c>RGBA8Unorm</c> ↔ <see cref="SKColorType.Rgba8888"/>, <c>BGRA8Unorm</c> ↔
    /// <see cref="SKColorType.Bgra8888"/>) or <c>SKSurface.Create</c> returns null.
    /// </item>
    /// </list>
    /// </summary>
    public sealed class MetalSkiaSurfaceFactory : IDisposable
    {
        /// <summary><c>MTLTextureUsageRenderTarget</c>.</summary>
        public const ulong TextureUsageRenderTarget = 0x4;

        GRContext _grContext = null!;

        public GRContext GRContext => _grContext;

        /// <param name="device">The host engine's <c>id&lt;MTLDevice&gt;</c>.</param>
        /// <param name="queue">The host engine's <c>id&lt;MTLCommandQueue&gt;</c>, created from <paramref name="device"/>.</param>
        public void InitializeFromNative(IntPtr device, IntPtr queue)
        {
            if (!MetalNative.IsApplePlatform)
                throw new PlatformNotSupportedException("Metal is only available on Apple platforms.");
            if (device == IntPtr.Zero)
                throw new ArgumentException("MTLDevice native pointer is null.", nameof(device));
            if (queue == IntPtr.Zero)
                throw new ArgumentException("MTLCommandQueue native pointer is null.", nameof(queue));
            if (!MetalNative.ConformsTo(device, "MTLDevice"))
                throw new ArgumentException("Pointer is not an id<MTLDevice>.", nameof(device));
            if (!MetalNative.ConformsTo(queue, "MTLCommandQueue"))
                throw new ArgumentException("Pointer is not an id<MTLCommandQueue>.", nameof(queue));

            using var backendContext = new GRMtlBackendContext
            {
                DeviceHandle = device,
                QueueHandle = queue,
            };

            _grContext = GRContext.CreateMetal(backendContext)
                ?? throw new InvalidOperationException("GRContext.CreateMetal failed.");
        }

        /// <summary>
        /// Wraps the host's <c>id&lt;MTLTexture&gt;</c> for Skia. The texture must carry
        /// <c>MTLTextureUsageRenderTarget</c>: Skia refuses to wrap one without it, and only says so
        /// by <c>SKSurface.Create</c> returning null, so this checks up front and names the problem.
        /// </summary>
        public MetalTextureState CreateTextureState(IntPtr texture)
        {
            if (texture == IntPtr.Zero)
                throw new ArgumentException("MTLTexture handle is null.", nameof(texture));
            if (!MetalNative.ConformsTo(texture, "MTLTexture"))
                throw new ArgumentException("Pointer is not an id<MTLTexture>.", nameof(texture));

            var usage = (ulong)MetalNative.SendNUInt(texture, MetalNative.SelUsage);
            if ((usage & TextureUsageRenderTarget) == 0)
                throw new ArgumentException(
                    $"MTLTexture usage 0x{usage:X} lacks MTLTextureUsageRenderTarget, which Skia needs to draw into it.",
                    nameof(texture));

            return new MetalTextureState(texture, new GRMtlTextureInfo(texture));
        }

        /// <summary>
        /// Creates the Skia surface backing a wrapped texture. Calls
        /// <c>GRContext.ResetContext(GRBackendState.All)</c> first, like the other Core factories, so
        /// Skia doesn't trust state cached from before the host last used the device.
        /// </summary>
        /// <param name="colorSpace">
        /// Optional Skia color-space tag - see the equivalent parameter on
        /// <c>VkSkiaSurfaceFactory.CreateSurface</c>. <c>null</c> is raw passthrough.
        /// </param>
        public (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            MetalTextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace = null)
        {
            _grContext.ResetContext(GRBackendState.All);

            var backendRT = new GRBackendRenderTarget(width, height, state.TextureInfo);

            var surface = SKSurface.Create(_grContext, backendRT, GRSurfaceOrigin.TopLeft, colorType, colorSpace)
                ?? throw new InvalidOperationException(
                    $"SKSurface.Create failed for Metal backend. Check that {colorType} matches the texture's MTLPixelFormat.");

            return (surface, backendRT);
        }

        /// <summary>
        /// Paired with <see cref="EndDraw(bool)"/>. A no-op today (no queue lock to take, see the class
        /// doc comment), kept so hosts drive every Core factory the same way.
        /// </summary>
        public void BeginDraw()
        {
        }

        /// <summary>Same as <see cref="EndDraw(bool)"/> with <c>synchronous: true</c>.</summary>
        public void EndDraw() => EndDraw(synchronous: true);

        /// <summary>
        /// Flushes Skia's recorded work and commits it to the shared queue. <paramref name="synchronous"/>
        /// <c>true</c> blocks until the GPU finishes, for a host about to read the texture from the CPU.
        /// A host whose own reads are committed after this on the same queue can pass <c>false</c>.
        /// </summary>
        public void EndDraw(bool synchronous)
        {
            _grContext.Flush(submit: true, synchronous: synchronous);
        }

        public void Dispose()
        {
            _grContext?.Dispose();
        }
    }
}
