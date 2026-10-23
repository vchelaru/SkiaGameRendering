using static SkiaGameRendering.Core.D3D12.D3D12Com;

namespace SkiaGameRendering.Core.D3D12
{
    /// <summary>
    /// Cuts an engine's open <c>ID3D12GraphicsCommandList</c> in two at a point of the host's choosing: submits
    /// what is recorded so far, and later reopens the very same list object on a fresh command allocator so
    /// the engine keeps recording into it. A host whose engine records a whole frame into one list and only
    /// submits at present needs this before Skia can draw into a target the engine has already drawn to,
    /// because Skia's work reaches the queue the moment its pass ends and would otherwise run underneath
    /// everything the engine recorded before it.
    /// <para>
    /// The caller owns everything that makes the cut invisible to the engine: <see cref="Reopen"/> returns the
    /// list in the empty recording state, with no root signature, descriptor heaps, render targets, pipeline
    /// state, viewport or buffers bound, and the host has to put back whatever its engine believes is bound
    /// (the <c>Set*</c> helpers here cover the ones every D3D12 engine sets).
    /// </para>
    /// <para>
    /// <b>Allocators.</b> The reopened list records into an allocator this class owns, not the one the engine
    /// attached to the list, which holds the first half and stays the engine's. A recycled allocator is reset
    /// only <see cref="RetireFrames"/> frames after it was handed out, by which point the engine has long
    /// finished the list that used it: pass the engine's own presented-frame counter to <see cref="Reopen"/>.
    /// Not thread-safe; one instance per device and queue, driven from the thread that records the list.
    /// </para>
    /// </summary>
    public sealed class D3D12CommandListSplitter : IDisposable
    {
        /// <summary>
        /// How many presented frames an allocator is held after <see cref="Reopen"/> before it is reset and
        /// reused. An engine keeps at most its swap chain's buffer count (2 or 3) frames in flight, so this
        /// is past any of them.
        /// </summary>
        public const int RetireFrames = 6;

        readonly IntPtr _device;
        readonly IntPtr _queue;
        readonly Stack<IntPtr> _free = new();
        readonly Queue<(IntPtr allocator, long frame)> _inUse = new();
        bool _disposed;

        /// <param name="device">The engine's <c>ID3D12Device*</c>.</param>
        /// <param name="queue">The engine's direct <c>ID3D12CommandQueue*</c>: the queue the list is submitted to.</param>
        public D3D12CommandListSplitter(IntPtr device, IntPtr queue)
        {
            if (device == IntPtr.Zero)
                throw new ArgumentException("D3D12 device native pointer is null.", nameof(device));
            if (queue == IntPtr.Zero)
                throw new ArgumentException("D3D12 command queue native pointer is null.", nameof(queue));

            _device = device;
            _queue = queue;
        }

        /// <summary>The number of allocators this instance has created and not yet released.</summary>
        public int AllocatorCount => _free.Count + _inUse.Count;

        /// <summary>
        /// Closes <paramref name="list"/> and executes it on the queue. Nothing waits: the GPU runs it in queue
        /// order ahead of whatever is submitted next, which is the point.
        /// </summary>
        public void Submit(IntPtr list)
        {
            ThrowIfDisposed();
            if (list == IntPtr.Zero)
                throw new ArgumentException("ID3D12GraphicsCommandList handle is null.", nameof(list));

            Close(list);
            ExecuteCommandList(_queue, list);
        }

        /// <summary>
        /// Reopens a list <see cref="Submit"/> closed, on a recycled or new allocator. <paramref name="frame"/>
        /// is the engine's count of presented frames, which only ever goes up.
        /// </summary>
        public void Reopen(IntPtr list, long frame)
        {
            ThrowIfDisposed();
            if (list == IntPtr.Zero)
                throw new ArgumentException("ID3D12GraphicsCommandList handle is null.", nameof(list));

            while (_inUse.Count > 0 && frame - _inUse.Peek().frame >= RetireFrames)
            {
                var retired = _inUse.Dequeue().allocator;
                ResetAllocator(retired);
                _free.Push(retired);
            }

            var allocator = _free.Count > 0 ? _free.Pop() : CreateCommandAllocator(_device);
            try
            {
                ResetList(list, allocator);
            }
            catch
            {
                Release(allocator);
                throw;
            }
            _inUse.Enqueue((allocator, frame));
        }

        /// <summary>ID3D12GraphicsCommandList::SetGraphicsRootSignature.</summary>
        public static unsafe void SetGraphicsRootSignature(IntPtr list, IntPtr rootSignature)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, void>)(*(void***)list)[30];
            fn(list, rootSignature);
        }

        /// <summary>ID3D12GraphicsCommandList::SetDescriptorHeaps with the two heaps D3D12 allows at once, a CBV/SRV/UAV heap and a sampler heap.</summary>
        public static unsafe void SetDescriptorHeaps(IntPtr list, IntPtr resourceHeap, IntPtr samplerHeap)
        {
            var heaps = stackalloc IntPtr[2] { resourceHeap, samplerHeap };
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, IntPtr*, void>)(*(void***)list)[28];
            fn(list, 2, heaps);
        }

        /// <summary>
        /// ID3D12GraphicsCommandList::OMSetRenderTargets for render targets in separate descriptors.
        /// <paramref name="depthStencilView"/> is a CPU descriptor handle, or 0 for no depth buffer.
        /// </summary>
        public static unsafe void SetRenderTargets(IntPtr list, ReadOnlySpan<nuint> renderTargetViews, nuint depthStencilView)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, nuint*, int, nuint*, void>)(*(void***)list)[46];
            fixed (nuint* rtvs = renderTargetViews)
            {
                var dsv = depthStencilView;
                fn(list, (uint)renderTargetViews.Length, rtvs, 0, depthStencilView == 0 ? null : &dsv);
            }
        }

        /// <summary>
        /// Releases the allocators. Waits for nothing: call it once the engine has finished every frame that
        /// used a reopened list, which is true at device teardown.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            while (_free.Count > 0)
                Release(_free.Pop());
            while (_inUse.Count > 0)
                Release(_inUse.Dequeue().allocator);
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(D3D12CommandListSplitter));
        }
    }
}
