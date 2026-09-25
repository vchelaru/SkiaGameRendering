using Godot;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;

namespace SkiaGameRendering.Godot
{
    /// <summary>
    /// The D3D12 backend - Godot's default driver for new Windows projects since 4.6. Hands Godot's
    /// own <c>IDXGIAdapter1</c>/<c>ID3D12Device</c>/<c>ID3D12CommandQueue</c> to
    /// <see cref="D3D12SkiaSurfaceFactory"/>. See <see cref="RenderingDeviceGodotBackend"/> for what the two RD
    /// backends share.
    ///
    /// <b>Not zero-copy, deliberately.</b> Godot's D3D12 driver allocates every texture with its
    /// typeless family format (<c>resource_desc.Format = RD_TO_D3D12_FORMAT[...].family</c> in
    /// <c>texture_create</c>) so it can create UNORM and sRGB views of the same resource, while Skia's
    /// D3D12 backend creates its render-target view with a null descriptor
    /// (<c>GrD3DCpuDescriptorManager::createRenderTargetView</c>), which D3D12 rejects for a typeless
    /// resource. Skia therefore draws into a typed <c>ID3D12Resource</c> this backend allocates
    /// (<see cref="D3D12SkiaSurfaceFactory.CreateRenderTargetResource"/>), and <see cref="Resources.EndFrame"/>
    /// queues one <c>CopyResource</c> into Godot's texture - a GPU-to-GPU copy of the same family
    /// (typed UNORM into TYPELESS is allowed), no CPU involvement. Two textures' worth of memory and
    /// one blit per frame is the cost. It also means Skia owns its surface outright: it persists
    /// across frames, nothing is re-wrapped, and Skia's own state tracking stays truthful because the
    /// copy returns Skia's resource to <c>RENDER_TARGET</c>.
    ///
    /// MAINTENANCE NOTES (read from Godot 4.7.2's source, not assumed):
    /// <list type="bullet">
    /// <item>
    /// <b>Godot's D3D12 driver tracks resource state two different ways</b>, chosen by
    /// <c>D3D12_FEATURE_D3D12_OPTIONS12.EnhancedBarriersSupported</c> at device creation. With
    /// enhanced barriers it uses the render graph's usage tracking (like Vulkan) and
    /// <c>D3D12_BARRIER_LAYOUT_SHADER_RESOURCE</c> for a sampled texture, whose legacy-state
    /// equivalent is <c>ALL_SHADER_RESOURCE</c>. Without them it keeps legacy per-subresource states
    /// and, when a uniform set is prepared for a draw, narrows a fragment-only sampled texture to
    /// <c>PIXEL_SHADER_RESOURCE</c> (<c>command_uniform_set_prepare_for_use</c>). <see cref="_restingState"/>
    /// is queried the same way Godot decides, so the per-frame copy returns Godot's texture to the
    /// exact state Godot believes it is in, and Godot's "is this transition redundant" check
    /// (<c>_resource_transition_batch</c>: redundant when the current state already has every bit of
    /// the wanted one) keeps it from ever emitting a barrier of its own again for canvas sampling.
    /// </item>
    /// <item>
    /// <b>Sampling only, from fragment shaders, on the legacy path.</b> If a project also samples the
    /// texture from a vertex or compute shader without enhanced barriers, Godot legitimately
    /// transitions it to a different shader-resource state and this backend's hand-back state no
    /// longer matches Godot's belief; the D3D12 debug layer reports the mismatch, hardware mostly
    /// tolerates it. Godot's 2D canvas (Sprite2D, TextureRect, CanvasItem shaders) is fragment-only.
    /// </item>
    /// <item>
    /// <b>The adapter Godot hands out is already an <c>IDXGIAdapter1</c></b>
    /// (<c>rendering_context_driver_d3d12.cpp</c> enumerates with <c>EnumAdapters1</c>/
    /// <c>EnumAdapterByGpuPreference</c>), which is what Skia's <c>GRD3DBackendContext.Adapter</c> wants.
    /// </item>
    /// </list>
    /// </summary>
    internal sealed class D3D12GodotBackend : RenderingDeviceGodotBackend
    {
        readonly D3D12SkiaSurfaceFactory _factory = new();
        D3D12ResourceTransitioner? _transitioner;
        IntPtr _device;
        uint _restingState;

        internal override string DriverName => "d3d12";

        internal override bool RendersIntoGodotTexture => false;

        /// <summary>Whether Godot's device runs with enhanced barriers - see this class's doc comment.</summary>
        internal bool EnhancedBarriers { get; private set; }

        protected override void InitializeCore()
        {
            var rd = RenderingDevice;
            var adapter = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.PhysicalDevice, default, 0);
            var device = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.LogicalDevice, default, 0);
            var queue = (IntPtr)rd.GetDriverResource(RenderingDevice.DriverResource.CommandQueue, default, 0);

            if (adapter == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null IDXGIAdapter (DriverResource.PhysicalDevice).");
            if (device == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null ID3D12Device (DriverResource.LogicalDevice).");
            if (queue == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null ID3D12CommandQueue (DriverResource.CommandQueue).");

            _device = device;
            EnhancedBarriers = D3D12SkiaSurfaceFactory.QueryEnhancedBarriersSupported(device);
            _restingState = EnhancedBarriers
                ? D3D12Constants.ResourceStateAllShaderResource
                : D3D12Constants.ResourceStatePixelShaderResource;

            _factory.InitializeFromNative(adapter, device, queue, acquireQueueLock: null);
            _transitioner = new D3D12ResourceTransitioner(device, queue);
        }

        internal override RenderingDeviceGpuResources CreateGpuResources(Rid texture, int width, int height, SKColorType colorType)
        {
            var godotResource = (IntPtr)RenderingDevice.GetDriverResource(RenderingDevice.DriverResource.Texture, texture, 0);
            if (godotResource == IntPtr.Zero)
                throw new InvalidOperationException("Godot RenderingDevice returned a null ID3D12Resource for the Skia texture (DriverResource.Texture).");
            return new Resources(this, godotResource, width, height, colorType);
        }

        internal override void WaitForPendingGpuWork() => _transitioner?.WaitForCompletion();

        protected override void DisposeCore()
        {
            _transitioner?.Dispose();
            _transitioner = null;
            _factory.Dispose();
        }

        /// <summary>
        /// The typed DXGI format Skia renders in for a color type. Godot's texture for the same
        /// <see cref="RenderingDevice.DataFormat"/> is the typeless family of this format, which is what
        /// makes the per-frame <c>CopyResource</c> legal.
        /// </summary>
        static uint ToDxgiFormat(SKColorType colorType) => colorType switch
        {
            SKColorType.Bgra8888 => D3D12Constants.FormatB8G8R8A8Unorm,
            SKColorType.Rgba1010102 => D3D12Constants.FormatR10G10B10A2Unorm,
            SKColorType.Rgba16161616 => D3D12Constants.FormatR16G16B16A16Unorm,
            SKColorType.Rgba8888 => D3D12Constants.FormatR8G8B8A8Unorm,
            _ => throw new NotSupportedException($"SkiaGameRendering.Godot does not support SKColorType.{colorType} on D3D12."),
        };

        /// <summary>One Skia-owned typed resource plus the persistent surface wrapping it. See the class doc comment for why the copy.</summary>
        sealed class Resources : RenderingDeviceGpuResources
        {
            readonly D3D12GodotBackend _backend;
            readonly IntPtr _godotResource;
            IntPtr _skiaResource;
            SKSurface? _surface;
            GRBackendRenderTarget? _renderTarget;

            internal Resources(D3D12GodotBackend backend, IntPtr godotResource, int width, int height, SKColorType colorType)
            {
                _backend = backend;
                _godotResource = godotResource;

                var format = ToDxgiFormat(colorType);
                _skiaResource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(backend._device, width, height, format);
                try
                {
                    var state = backend._factory.CreateTextureState(_skiaResource, format, D3D12Constants.ResourceStateRenderTarget);
                    (_surface, _renderTarget) = backend._factory.CreateSurface(state, width, height, colorType);
                }
                catch
                {
                    D3D12SkiaSurfaceFactory.ReleaseResource(_skiaResource);
                    _skiaResource = IntPtr.Zero;
                    throw;
                }
            }

            internal override SKSurface BeginFrame()
            {
                _backend._factory.BeginDraw();
                return _surface ?? throw new ObjectDisposedException(nameof(Resources));
            }

            internal override void EndFrame()
            {
                try
                {
                    if (_surface != null)
                    {
                        _surface.Canvas.RestoreToCount(1);
                        _surface.Flush();
                    }
                }
                finally
                {
                    // No CPU wait: the copy below and Godot's own sampling are queue-ordered behind it.
                    _backend._factory.EndDraw(synchronous: false);
                }

                // Skia left its resource in RENDER_TARGET (its own belief, restored below); Godot's
                // texture sits in the resting state Godot believes - both restored after the copy.
                _backend._transitioner!.CopyWithTransitions(
                    destination: _godotResource,
                    destinationStateBefore: _backend._restingState,
                    destinationStateAfter: _backend._restingState,
                    source: _skiaResource,
                    sourceStateBefore: D3D12Constants.ResourceStateRenderTarget,
                    sourceStateAfter: D3D12Constants.ResourceStateRenderTarget);
            }

            public override void Dispose()
            {
                _backend._factory.BeginDraw();
                try
                {
                    _surface?.Dispose();
                    _surface = null;
                    _renderTarget?.Dispose();
                    _renderTarget = null;
                    // Skia drops its reference to the wrapped resource asynchronously once its work is
                    // done; the caller already waited for the last hand-back, so nothing is in flight.
                    _backend._factory.GRContext.PurgeResources();
                }
                finally
                {
                    _backend._factory.EndDraw(synchronous: true);
                }
                D3D12SkiaSurfaceFactory.ReleaseResource(_skiaResource);
                _skiaResource = IntPtr.Zero;
            }
        }
    }
}
