using System.Reflection;
using System.Threading;
using SkiaGameRendering.Core.D3D12;
using SkiaSharp;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Stride.Graphics;
using BarrierLayout = Stride.Graphics.BarrierLayout;

namespace SkiaGameRendering.Stride.D3D12
{
    /// <summary>
    /// Stride-specific adapter over <see cref="D3D12SkiaSurfaceFactory"/> (see that class for how the
    /// D3D12 interop itself works). Pulls Stride's adapter, device and direct queue out and hands them
    /// to the shared factory, wires Stride's own queue lock through so Skia's and Stride's
    /// <c>ExecuteCommandLists</c> calls never overlap, and hands each texture between Stride's barrier
    /// tracking and Skia's.
    ///
    /// MAINTENANCE NOTES (read from the <c>StrideGraphicsApi=Direct3D12</c> build of Stride.Graphics
    /// 4.4.0-beta5 by decompiling it; every member reached by name is pinned by
    /// <c>tests/Tests.Stride.D3D12/StrideD3D12ReflectionTests.cs</c>):
    /// <list type="bullet">
    /// <item>
    /// <b>Everything is reflected, even the public <c>GraphicsDevice.NativeDevice</c>.</b> This project
    /// compiles against the Direct3D11 variant of Stride.Graphics (the Windows default), where that
    /// property is a <c>ComPtr&lt;ID3D11Device&gt;</c>. The members used: <c>GraphicsDevice.NativeDevice</c>
    /// (public), <c>NativeCommandQueue</c> (internal property) and <c>QueueLock</c> (internal field);
    /// <c>GraphicsAdapter.NativeAdapter</c> (internal, already the <c>IDXGIAdapter1</c> that
    /// <c>GRD3DBackendContext.Adapter</c> wants); and <c>GraphicsResourceBase.NativeResource</c>
    /// (protected internal).
    /// </item>
    /// <item>
    /// <b>One queue, one lock.</b> Stride submits all of its work, texture uploads included, on the
    /// single direct queue behind <c>NativeCommandQueue</c>, always inside <c>lock (QueueLock)</c>.
    /// Skia's flush and <see cref="D3D12ResourceTransitioner"/>'s submissions take that same monitor,
    /// and ordering between them comes from queue order, so no fence is shared.
    /// </item>
    /// <item>
    /// <b>Stride uses enhanced barriers; Skia uses legacy ones; the handoff is at COMMON.</b> Stride
    /// records only <c>ID3D12GraphicsCommandList7::Barrier</c>, from the layout it tracks per resource.
    /// Skia records legacy <c>ResourceBarrier</c>s from the state it was told at surface creation
    /// (<c>RENDER_TARGET</c>) and keeps believing. The D3D12 debug layer rejects a resource crossing
    /// between the two models in any layout but <c>COMMON</c> ("Interop between legacy ResourceBarrier
    /// and enhanced Barrier commands require texture resources to be in BARRIER_LAYOUT_COMMON or
    /// RESOURCE_STATE_COMMON"), so <see cref="AcquireForSkia"/> has Stride move the texture to
    /// <c>COMMON</c> with its own barrier, then moves it on to <c>RENDER_TARGET</c> with a legacy one;
    /// <see cref="ReleaseToStride"/> returns it to <c>COMMON</c>, where Stride's tracker already has it.
    /// That costs three small submissions per draw.
    /// </item>
    /// <item>
    /// <b>Stride's D3D12 <c>SpriteBatch</c> does not transition what it samples.</b> Its callers do
    /// (Stride.Rendering's <c>ImageEffect</c>, <c>RenderTextureSceneRenderer</c>), so the composite in
    /// <see cref="SkiaStrideD3D12RenderTarget2D"/> records the move to <c>ShaderResource</c> itself.
    /// </item>
    /// <item>
    /// <b>Skia submits immediately; Stride submits at the end of its command list.</b> A Skia draw runs
    /// on the GPU before anything the current, not-yet-submitted Stride command list recorded earlier.
    /// That is harmless as long as nothing in that command list touched this texture earlier in the
    /// same frame, which holds for the Begin/draw/End-then-composite pattern this package supports.
    /// </item>
    /// </list>
    /// </summary>
    internal sealed class SkiaStrideD3D12Context : IDisposable
    {
        const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        readonly D3D12SkiaSurfaceFactory _factory = new();
        D3D12ResourceTransitioner? _transitioner;
        GraphicsDevice? _graphicsDevice;
        CommandList? _handoffCommandList;

        static PropertyInfo? _nativeDeviceProperty;
        static PropertyInfo? _nativeCommandQueueProperty;
        static FieldInfo? _queueLockField;
        static PropertyInfo? _nativeAdapterProperty;
        static PropertyInfo? _nativeResourceProperty;

        internal GRContext GRContext => _factory.GRContext;

        internal unsafe void Initialize(GraphicsDevice graphicsDevice)
        {
            _nativeDeviceProperty ??= typeof(GraphicsDevice).GetProperty("NativeDevice", PublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsDevice.NativeDevice.");
            _nativeCommandQueueProperty ??= typeof(GraphicsDevice).GetProperty("NativeCommandQueue", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsDevice.NativeCommandQueue.");
            _queueLockField ??= typeof(GraphicsDevice).GetField("QueueLock", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsDevice.QueueLock.");
            _nativeAdapterProperty ??= typeof(GraphicsAdapter).GetProperty("NativeAdapter", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsAdapter.NativeAdapter.");

            if (_nativeDeviceProperty.PropertyType != typeof(ComPtr<ID3D12Device>))
                throw new InvalidOperationException(
                    $"Stride's GraphicsDevice.NativeDevice is {_nativeDeviceProperty.PropertyType}, not an ID3D12Device. " +
                    "SkiaGameRendering.Stride.D3D12 needs <StrideGraphicsApi>Direct3D12</StrideGraphicsApi> in the game's project.");

            var device = (IntPtr)((ComPtr<ID3D12Device>)_nativeDeviceProperty.GetValue(graphicsDevice)!).Handle;
            var queue = (IntPtr)((ComPtr<ID3D12CommandQueue>)_nativeCommandQueueProperty.GetValue(graphicsDevice)!).Handle;
            var adapter = (IntPtr)((ComPtr<IDXGIAdapter1>)_nativeAdapterProperty.GetValue(graphicsDevice.Adapter)!).Handle;
            var queueLock = _queueLockField.GetValue(graphicsDevice)
                ?? throw new Exception("Stride GraphicsDevice.QueueLock is null.");

            if (device == IntPtr.Zero)
                throw new Exception("Stride GraphicsDevice.NativeDevice is null.");
            if (queue == IntPtr.Zero)
                throw new Exception("Stride GraphicsDevice.NativeCommandQueue is null.");
            if (adapter == IntPtr.Zero)
                throw new Exception("Stride GraphicsAdapter.NativeAdapter is null.");

            Func<IDisposable> acquireQueueLock = () => AcquireQueueLock(queueLock);
            _factory.InitializeFromNative(adapter, device, queue, acquireQueueLock);
            _transitioner = new D3D12ResourceTransitioner(device, queue, acquireQueueLock);
            _graphicsDevice = graphicsDevice;
            _handoffCommandList = CommandList.New(graphicsDevice);
        }

        static IDisposable AcquireQueueLock(object queueLock)
        {
            Monitor.Enter(queueLock);
            return new QueueLockRelease(queueLock);
        }

        sealed class QueueLockRelease(object queueLock) : IDisposable
        {
            public void Dispose() => Monitor.Exit(queueLock);
        }

        /// <summary>Takes Stride's queue lock, held until <see cref="EndDraw"/>.</summary>
        internal void BeginDraw() => _factory.BeginDraw();

        /// <summary>
        /// Submits Skia's work without a CPU wait and releases the queue lock. Stride's own use of the
        /// texture afterwards is queued behind it on the same queue.
        /// </summary>
        internal void EndDraw() => _factory.EndDraw(synchronous: false);

        /// <summary>
        /// Moves <paramref name="texture"/> from whatever layout Stride left it in to
        /// <c>RENDER_TARGET</c>, via <c>COMMON</c>. See this class's doc comment. Call between
        /// <see cref="BeginDraw"/> and <see cref="EndDraw"/>, before Skia records anything.
        /// </summary>
        internal void AcquireForSkia(Texture texture)
        {
            var commandList = _handoffCommandList!;
            commandList.Reset();
            commandList.ResourceBarrierTransition(texture, BarrierLayout.Common);
            _graphicsDevice!.ExecuteCommandList(commandList.Close());

            _transitioner!.Transition(
                GetNativeResource(texture), D3D12Constants.ResourceStateCommon, D3D12Constants.ResourceStateRenderTarget);
        }

        /// <summary>
        /// Queues the move from <c>RENDER_TARGET</c> back to <c>COMMON</c>, where Stride's tracker has
        /// had the texture since <see cref="AcquireForSkia"/>. Call after <see cref="EndDraw"/>.
        /// </summary>
        internal void ReleaseToStride(Texture texture) =>
            _transitioner!.Transition(
                GetNativeResource(texture), D3D12Constants.ResourceStateRenderTarget, D3D12Constants.ResourceStateCommon);

        /// <summary>Blocks until Skia's and the transitioner's queued GPU work has finished, for teardown.</summary>
        internal void WaitForIdle()
        {
            _factory.BeginDraw();
            _factory.EndDraw(synchronous: true);
            _transitioner?.WaitForCompletion();
        }

        /// <summary>Wraps a texture <see cref="AcquireForSkia"/> has put in <c>RENDER_TARGET</c>.</summary>
        internal D3D12TextureState CreateTextureState(Texture texture) =>
            // Stride's PixelFormat values are DXGI_FORMAT values (pinned by the reflection tests).
            _factory.CreateTextureState(GetNativeResource(texture), (uint)texture.Format, D3D12Constants.ResourceStateRenderTarget);

        internal (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            D3D12TextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace = null) =>
            _factory.CreateSurface(state, width, height, colorType, colorSpace);

        static unsafe IntPtr GetNativeResource(Texture texture)
        {
            _nativeResourceProperty ??= typeof(GraphicsResourceBase).GetProperty("NativeResource", NonPublicInstance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsResourceBase.NativeResource.");

            var resource = (IntPtr)((ComPtr<ID3D12Resource>)_nativeResourceProperty.GetValue(texture)!).Handle;
            if (resource == IntPtr.Zero)
                throw new Exception($"ID3D12Resource is null on Texture ({texture.Width}x{texture.Height}).");
            return resource;
        }

        public void Dispose()
        {
            _transitioner?.Dispose();
            _transitioner = null;
            _handoffCommandList?.Dispose();
            _handoffCommandList = null;
            _factory.Dispose();
        }
    }
}
