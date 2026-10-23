using SkiaSharp;

namespace SkiaGameRendering.Core.D3D12
{
    /// <summary>
    /// A single host-supplied <c>ID3D12Resource</c> wrapped for Skia, plus the D3D12 metadata Skia
    /// needs to draw into it. Returned by <see cref="D3D12SkiaSurfaceFactory.CreateTextureState"/> and
    /// consumed by <see cref="D3D12SkiaSurfaceFactory.CreateSurface"/>.
    /// </summary>
    public sealed class D3D12TextureState
    {
        internal D3D12TextureState(IntPtr resource, GRD3DTextureResourceInfo resourceInfo)
        {
            Resource = resource;
            ResourceInfo = resourceInfo;
        }

        /// <summary>The wrapped <c>ID3D12Resource*</c>, for the host's own bookkeeping.</summary>
        public IntPtr Resource { get; }

        internal GRD3DTextureResourceInfo ResourceInfo { get; }
    }

    /// <summary>
    /// What <see cref="D3D12SkiaSurfaceFactory.DescribeDirectTarget"/> found out about an engine's current
    /// render target resource, checked once so a host can refuse before it disturbs anything.
    /// </summary>
    public readonly struct D3D12DirectTarget
    {
        internal D3D12DirectTarget(IntPtr resource, uint format, int width, int height, SKColorType colorType)
        {
            Resource = resource;
            Format = format;
            Width = width;
            Height = height;
            ColorType = colorType;
        }

        internal IntPtr Resource { get; }
        internal uint Format { get; }
        internal int Width { get; }
        internal int Height { get; }

        /// <summary>The Skia pixel layout matching the resource's <c>DXGI_FORMAT</c>.</summary>
        public SKColorType ColorType { get; }
    }

    /// <summary>One <see cref="D3D12SkiaSurfaceFactory.BeginDirectDraw"/> pass, ended by <see cref="D3D12SkiaSurfaceFactory.EndDirectDraw"/>.</summary>
    public sealed class D3D12DirectPass
    {
        internal D3D12DirectPass(SKSurface surface, GRBackendRenderTarget renderTarget, int saveCount)
        {
            Surface = surface;
            RenderTarget = renderTarget;
            SaveCount = saveCount;
        }

        internal SKSurface Surface { get; }
        internal GRBackendRenderTarget RenderTarget { get; }
        internal int SaveCount { get; }
    }

    /// <summary>
    /// Engine-agnostic D3D12/Skia interop, built on Skia's Ganesh D3D12 backend. Google has flagged
    /// Ganesh (Skia's whole GPU-backend generation, D3D12 included) for eventual replacement by
    /// Graphite - a real cost, knowingly accepted here rather than waited out, per issue #24: new
    /// backends have been cheap enough to build that reaching the users already on D3D12-targeting
    /// engine runtimes (MonoGame 3.8.5's native <c>WindowsDX12</c>, Stride's D3D12 mode) is worth
    /// paying for now.
    ///
    /// How it works: like <c>Core.VK</c> and unlike <c>Core.ANGLE</c>, this creates nothing of its
    /// own - the host engine already has an <c>ID3D12Device</c>/<c>ID3D12CommandQueue</c>, and Skia's
    /// <c>GrD3DGpu</c> is handed those same raw pointers directly via
    /// <see cref="SkiaSharp.GRD3DBackendContext"/>. Skia's draw commands and the host's own commands
    /// both submit to the SAME <c>ID3D12CommandQueue</c> - there is no separate context to own or
    /// texture-import step the way ANGLE needs <c>eglCreateDeviceANGLE</c>/
    /// <c>eglCreatePbufferFromClientBuffer</c>; wrapping an <c>ID3D12Resource</c> is just constructing
    /// a <see cref="SkiaSharp.GRD3DTextureResourceInfo"/> that describes it (see
    /// <see cref="CreateTextureState"/>).
    ///
    /// MAINTENANCE NOTES:
    /// <list type="bullet">
    /// <item>
    /// <b>No interop library on either side, and no native shim file either.</b> Unlike
    /// <c>Core.VK</c>'s <c>VulkanNative</c> (which resolves the system Vulkan loader and its two
    /// bootstrap proc-address entry points), D3D12 needs no equivalent here: it is COM-vtable based,
    /// so Skia calls straight through the raw <c>Device</c>/<c>Queue</c> pointers handed to
    /// <see cref="SkiaSharp.GRD3DBackendContext"/> - there is no dynamic-loader/proc-address layer for
    /// D3D12 the way Vulkan's spec requires. This is a deliberate difference from Core.VK's shape, not
    /// a missing file.
    /// </item>
    /// <item>
    /// <b>Queue access must be externally synchronized.</b> <c>ID3D12CommandQueue::ExecuteCommandLists</c>
    /// is not documented thread-safe against concurrent calls on the same queue, and Skia's
    /// <c>GrD3DGpu</c> submits to the exact same <c>ID3D12CommandQueue</c> the host engine submits its
    /// own work to. Same reasoning <c>VkSkiaSurfaceFactory</c> gives for <c>vkQueueSubmit</c>:
    /// <c>Core.D3D12</c> cannot invent a shared lock out of nothing, so <see cref="InitializeFromNative"/>
    /// takes the same optional <c>acquireQueueLock</c> hook, invoked/disposed by
    /// <see cref="BeginDraw"/>/<see cref="EndDraw(bool)"/> around the one call that actually submits to the
    /// queue (<c>GRContext.Flush(submit: true, ...)</c>). Passing <c>null</c> means "no external
    /// synchronization" - the caller's responsibility either way; this library has no way to verify it.
    /// </item>
    /// <item>
    /// <b>The post-draw <c>D3D12_RESOURCE_STATES</c> cannot be read back through this SkiaSharp
    /// version.</b> See the doc comment on <see cref="EndDraw(bool)"/> - verified by listing SkiaSharp
    /// 3.119.4's actual native P/Invoke surface (<c>SkiaApi</c>), not assumed: there is no
    /// <c>gr_backendrendertarget_get_d3d_*</c> entry point and no <c>GrBackendSurfaceMutableState</c>
    /// binding, the same absence <c>VkSkiaSurfaceFactory.EndDraw</c> documents for Vulkan's
    /// <c>VkImageLayout</c>.
    /// </item>
    /// </list>
    /// </summary>
    public sealed class D3D12SkiaSurfaceFactory : IDisposable
    {
        GRContext _grContext = null!;
        Func<IDisposable>? _acquireQueueLock;
        IDisposable? _queueLockHandle;

        public GRContext GRContext => _grContext;

#if NET8_0_OR_GREATER
        /// <summary>
        /// Whether the host's device (and the D3D12 runtime under it) supports enhanced barriers.
        /// An engine that has them tracks textures by <c>D3D12_BARRIER_LAYOUT</c> rather than legacy
        /// <c>D3D12_RESOURCE_STATES</c> (Godot's D3D12 driver does exactly this switch), which changes
        /// the legacy state a host adapter must hand a resource back in - see the Godot backend.
        /// </summary>
        public static bool QueryEnhancedBarriersSupported(IntPtr device)
        {
            if (device == IntPtr.Zero)
                throw new ArgumentException("D3D12 device native pointer is null.", nameof(device));
            return D3D12Com.CheckEnhancedBarriersSupported(device);
        }

        /// <summary>
        /// Allocates a typed, render-target-capable <c>ID3D12Resource</c> on the host's device,
        /// starting in <c>D3D12_RESOURCE_STATE_RENDER_TARGET</c>, ready for
        /// <see cref="CreateTextureState"/>. For hosts whose own textures Skia cannot render into
        /// directly - Skia's D3D12 backend creates its render-target view with a null descriptor, so
        /// the resource's own format must be a typed one, and Godot allocates every texture with the
        /// typeless family format. Release it with <see cref="ReleaseResource"/>.
        /// </summary>
        public static IntPtr CreateRenderTargetResource(IntPtr device, int width, int height, uint dxgiFormat)
        {
            if (device == IntPtr.Zero)
                throw new ArgumentException("D3D12 device native pointer is null.", nameof(device));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            var desc = new D3D12Com.D3D12_RESOURCE_DESC
            {
                Dimension = D3D12Com.D3D12_RESOURCE_DIMENSION_TEXTURE2D,
                Alignment = 0,
                Width = (ulong)width,
                Height = (uint)height,
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = dxgiFormat,
                SampleDesc = new D3D12Com.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Layout = D3D12Com.D3D12_TEXTURE_LAYOUT_UNKNOWN,
                Flags = D3D12Com.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,
            };
            var heap = new D3D12Com.D3D12_HEAP_PROPERTIES { Type = D3D12Com.D3D12_HEAP_TYPE_DEFAULT };
            return D3D12Com.CreateCommittedResource(device, heap, desc, D3D12Constants.ResourceStateRenderTarget);
        }

        /// <summary>Releases a resource from <see cref="CreateRenderTargetResource"/> (one <c>IUnknown::Release</c>).</summary>
        public static void ReleaseResource(IntPtr resource)
        {
            if (resource != IntPtr.Zero)
                D3D12Com.Release(resource);
        }
#endif

        /// <param name="adapter">
        /// The host's <c>IDXGIAdapter1*</c> (or <c>IDXGIAdapter*</c>) the device was created against.
        /// </param>
        /// <param name="device">The host engine's <c>ID3D12Device*</c>.</param>
        /// <param name="queue">
        /// The host engine's <c>ID3D12CommandQueue*</c> - the SAME queue the host submits its own
        /// work to. See the queue-lock discussion in this class's doc comment.
        /// </param>
        /// <param name="acquireQueueLock">
        /// Optional hook returning an <see cref="IDisposable"/> that releases whatever lock the host
        /// uses to serialize its own <c>ExecuteCommandLists</c> calls against <paramref name="queue"/>.
        /// See the queue-lock discussion in this class's doc comment. Pass <c>null</c> for no external
        /// synchronization.
        /// </param>
        public void InitializeFromNative(
            IntPtr adapter, IntPtr device, IntPtr queue, Func<IDisposable>? acquireQueueLock = null)
        {
            if (adapter == IntPtr.Zero)
                throw new ArgumentException("D3D12 adapter native pointer is null.", nameof(adapter));
            if (device == IntPtr.Zero)
                throw new ArgumentException("D3D12 device native pointer is null.", nameof(device));
            if (queue == IntPtr.Zero)
                throw new ArgumentException("D3D12 command queue native pointer is null.", nameof(queue));

            _acquireQueueLock = acquireQueueLock;

            var backendContext = new GRD3DBackendContext
            {
                Adapter = adapter,
                Device = device,
                Queue = queue,
            };

            _grContext = GRContext.CreateDirect3D(backendContext)
                ?? throw new InvalidOperationException("GRContext.CreateDirect3D failed.");
        }

        /// <summary>
        /// Wraps the host's already-created <c>ID3D12Resource</c> as a
        /// <see cref="SkiaSharp.GRD3DTextureResourceInfo"/>, the D3D12 analog of
        /// <c>VkSkiaSurfaceFactory.CreateTextureState</c>. As with Vulkan, there is no separate
        /// "import" call - Skia treats any resource it's given a matching
        /// <see cref="SkiaSharp.GRD3DTextureResourceInfo"/> for as already usable, since it operates
        /// directly against the host's device/queue rather than a context of its own.
        /// </summary>
        /// <param name="resource">The host-owned <c>ID3D12Resource</c> handle to wrap.</param>
        /// <param name="format">The resource's <c>DXGI_FORMAT</c>.</param>
        /// <param name="resourceState">
        /// The resource's CURRENT <c>D3D12_RESOURCE_STATES</c> at the moment this call is made - Skia
        /// reads this once, as an input, to know what state to transition from for its first internal
        /// barrier. See <see cref="EndDraw(bool)"/> for why this library cannot report back what state the
        /// resource ends up in afterward.
        /// </param>
        /// <param name="sampleCount">The resource's own multisample count - almost always 1 for a render target.</param>
        /// <param name="levelCount">The resource's mip level count - almost always 1 for a render target.</param>
        public D3D12TextureState CreateTextureState(
            IntPtr resource, uint format, uint resourceState, uint sampleCount = 1, uint levelCount = 1)
        {
            if (resource == IntPtr.Zero)
                throw new ArgumentException("ID3D12Resource handle is null.", nameof(resource));

            var resourceInfo = new GRD3DTextureResourceInfo
            {
                Resource = resource,
                ResourceState = resourceState,
                Format = format,
                SampleCount = sampleCount,
                LevelCount = levelCount,
                SampleQualityPattern = 0,
                Protected = false,
            };

            return new D3D12TextureState(resource, resourceInfo);
        }

        /// <summary>
        /// Creates the Skia surface backing a wrapped <c>ID3D12Resource</c>, the D3D12 analog of
        /// <c>VkSkiaSurfaceFactory.CreateSurface</c>. Calls
        /// <c>GRContext.ResetContext(GRBackendState.All)</c> first, for the same reason
        /// <c>VkSkiaSurfaceFactory.CreateSurface</c> and <c>AngleSkiaSurfaceFactory.CreateSurface</c>
        /// both do: the shared device/queue's state can be mutated by whatever the host or another
        /// library did with it between draws.
        /// </summary>
        /// <param name="colorSpace">
        /// Optional Skia color-space tag for the surface - see the equivalent parameter on
        /// <c>VkSkiaSurfaceFactory.CreateSurface</c> for what this does and does not affect. Pass
        /// <c>null</c> (the default) for raw passthrough.
        /// </param>
        public (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            D3D12TextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace = null)
        {
            _grContext.ResetContext(GRBackendState.All);

            var backendRT = new GRBackendRenderTarget(width, height, state.ResourceInfo);

            var surface = SKSurface.Create(_grContext, backendRT, GRSurfaceOrigin.TopLeft, colorType, colorSpace)
                ?? throw new InvalidOperationException("SKSurface.Create failed for Direct3D backend.");

            return (surface, backendRT);
        }

        /// <summary>
        /// Acquires the host's queue lock (if one was wired through <c>acquireQueueLock</c> on
        /// <see cref="InitializeFromNative"/>) before any drawing happens. Paired with
        /// <see cref="EndDraw(bool)"/>.
        /// </summary>
        public void BeginDraw()
        {
            _queueLockHandle = _acquireQueueLock?.Invoke();
        }

        /// <summary>Same as <see cref="EndDraw(bool)"/> with <c>synchronous: true</c>.</summary>
        public void EndDraw() => EndDraw(synchronous: true);

        /// <summary>
        /// Flushes Skia's recorded D3D12 commands and submits them to the shared
        /// <c>ID3D12CommandQueue</c> (<c>GRContext.Flush(submit: true, synchronous)</c>) - the
        /// one call in this whole class that actually calls <c>ExecuteCommandLists</c>, which is why
        /// it (and not, say, <see cref="CreateSurface"/>) is what <see cref="BeginDraw"/>'s queue lock
        /// brackets. <paramref name="synchronous"/> <c>true</c> (what <see cref="EndDraw()"/> passes) blocks until the GPU
        /// finishes, matching <c>VkSkiaSurfaceFactory.EndDraw</c> and for the same reason: a host about
        /// to read the resource from the CPU needs the GPU work to have actually landed first. A host
        /// whose own consumption is queued behind this on the SAME queue can pass <c>false</c> and skip
        /// the stall, as the Godot backend does.
        /// <para>
        /// <b>This cannot tell the caller what <c>D3D12_RESOURCE_STATES</c> the resource ends up in.</b>
        /// Verified directly against SkiaSharp 3.119.4's native P/Invoke surface (<c>SkiaApi</c>), not
        /// assumed: there is no <c>gr_backendrendertarget_get_d3d_textureresourceinfo</c> native entry
        /// point and no <c>GrBackendSurfaceMutableState</c> binding either - the same absence
        /// <c>VkSkiaSurfaceFactory.EndDraw</c> documents for Vulkan's <c>VkImageLayout</c>.
        /// <see cref="D3D12TextureState"/>'s <c>ResourceInfo.ResourceState</c> is therefore a one-shot
        /// input set once in <see cref="CreateTextureState"/>, not a live handle.
        /// </para>
        /// <para>
        /// A resource being drawn into by Skia's D3D12 backend must sit in
        /// <c>D3D12_RESOURCE_STATE_RENDER_TARGET</c> while Skia's draw commands execute, and nothing
        /// in this flush path transitions it anywhere else afterward - so
        /// <c>D3D12_RESOURCE_STATE_RENDER_TARGET</c> is the ASSUMED post-<see cref="EndDraw(bool)"/> state
        /// for such a resource, not a value this library can verify or guarantee. A host needing
        /// certainty must insert its own <c>ResourceBarrier</c> rather than trust a reported value,
        /// since none exists.
        /// </para>
        /// </summary>
        public void EndDraw(bool synchronous)
        {
            try
            {
                _grContext.Flush(submit: true, synchronous: synchronous);
            }
            finally
            {
                _queueLockHandle?.Dispose();
                _queueLockHandle = null;
            }
        }

        // --- Drawing straight into the engine's current target (direct draw) ---
        //
        // D3D12 has no "currently bound render target" to ask the device for, so the host adapter finds the
        // engine's current target resource itself and hands it over. Everything else is the same as a
        // render-target draw: Skia draws into a wrapped ID3D12Resource on the engine's queue, which must be in
        // RENDER_TARGET when BeginDirectDraw is called (the host transitions it, see D3D12ResourceTransitioner)
        // and is left there by EndDirectDraw. Ordering against the engine's own work is the host's job too: Skia
        // submits at EndDirectDraw, so anything the engine recorded earlier has to be submitted before
        // BeginDirectDraw for it to land underneath Skia's pixels.

#if NET8_0_OR_GREATER
        // Reads the resource's descriptor through D3D12Com, which the netstandard2.1 (Unity) build leaves out.
        /// <summary>
        /// Checks that <paramref name="resource"/> is something Skia can draw into directly, and says what it
        /// is. Throws <see cref="NotSupportedException"/> for a resource that is not a single-sample 2D texture
        /// with the render-target flag, or whose format is not 8-bit RGBA or BGRA UNORM: Skia creates its render
        /// target view with the resource's own format, so a typeless format has none and an sRGB one would have
        /// Skia encode what it writes. Throws <see cref="InvalidOperationException"/> if the resource is not
        /// <paramref name="expectedWidth"/> by <paramref name="expectedHeight"/>, which means the engine's target
        /// is a view of something other than a whole texture.
        /// </summary>
        public static D3D12DirectTarget DescribeDirectTarget(IntPtr resource, int expectedWidth, int expectedHeight)
        {
            if (resource == IntPtr.Zero)
                throw new ArgumentException("ID3D12Resource handle is null.", nameof(resource));

            var desc = D3D12Com.GetResourceDesc(resource);
            if (desc.Dimension != D3D12Com.D3D12_RESOURCE_DIMENSION_TEXTURE2D)
                throw new NotSupportedException("SkiaDirectCanvas draws into a 2D render target; the engine's target is not a 2D texture.");
            if ((desc.Flags & D3D12Com.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET) == 0)
                throw new NotSupportedException("The engine's target resource does not allow render target use.");
            if (desc.SampleDesc.Count != 1)
                throw new NotSupportedException($"SkiaDirectCanvas does not draw into multisampled targets ({desc.SampleDesc.Count} samples).");
            if (desc.DepthOrArraySize != 1)
                throw new NotSupportedException($"SkiaDirectCanvas draws into a single 2D texture, not an array of {desc.DepthOrArraySize}.");

            SKColorType colorType;
            switch (desc.Format)
            {
                case D3D12Constants.FormatR8G8B8A8Unorm:
                    colorType = SKColorType.Rgba8888;
                    break;
                case D3D12Constants.FormatB8G8R8A8Unorm:
                    colorType = SKColorType.Bgra8888;
                    break;
                default:
                    throw new NotSupportedException(
                        $"SkiaDirectCanvas supports R8G8B8A8_UNORM and B8G8R8A8_UNORM targets, not DXGI_FORMAT {desc.Format} " +
                        "(typeless, sRGB-format and HDR targets are refused).");
            }

            if (desc.Width != (ulong)expectedWidth || desc.Height != (uint)expectedHeight)
                throw new InvalidOperationException(
                    $"The engine says its target is {expectedWidth}x{expectedHeight}, but the resource is {desc.Width}x{desc.Height}.");

            return new D3D12DirectTarget(resource, desc.Format, expectedWidth, expectedHeight, colorType);
        }
#endif

        /// <summary>
        /// Starts a pass that draws into the resource <paramref name="target"/> describes, which the host has
        /// already put in <c>RENDER_TARGET</c>, and returns its canvas. Takes the host's queue lock until
        /// <see cref="EndDirectDraw"/>. Does not clear.
        /// </summary>
        public SKCanvas BeginDirectDraw(in D3D12DirectTarget target, out D3D12DirectPass pass)
        {
            BeginDraw();
            try
            {
                var state = CreateTextureState(target.Resource, target.Format, D3D12Constants.ResourceStateRenderTarget);
                var (surface, renderTarget) = CreateSurface(state, target.Width, target.Height, target.ColorType);
                pass = new D3D12DirectPass(surface, renderTarget, surface.Canvas.Save());
                return surface.Canvas;
            }
            catch
            {
                _queueLockHandle?.Dispose();
                _queueLockHandle = null;
                throw;
            }
        }

        /// <summary>
        /// Ends a pass started by <see cref="BeginDirectDraw"/>: submits Skia's draws to the shared queue and
        /// releases the queue lock, even if it throws. The resource is left in <c>RENDER_TARGET</c>.
        /// <paramref name="synchronous"/> blocks until the GPU has finished, as for <see cref="EndDraw(bool)"/>.
        /// </summary>
        public void EndDirectDraw(D3D12DirectPass pass, bool synchronous = false)
        {
            try
            {
                pass.Surface.Canvas.RestoreToCount(pass.SaveCount);
                pass.Surface.Flush();
                pass.Surface.Dispose();
                pass.RenderTarget.Dispose();
            }
            finally
            {
                EndDraw(synchronous);
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
