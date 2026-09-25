using static SkiaGameRendering.Core.D3D12.D3D12Com;

namespace SkiaGameRendering.Core.D3D12
{
    /// <summary>
    /// Records and submits small command lists on the host's <c>ID3D12CommandQueue</c> - resource
    /// state transitions, and a copy bracketed by transitions - so a host adapter can reconcile the
    /// <c>D3D12_RESOURCE_STATES</c> Skia leaves a resource in with what the host engine's own state
    /// tracking expects, without owning any D3D12 command plumbing itself. The D3D12 sibling of
    /// Core.VK's <c>VkImageLayoutTransitioner</c>, for the reason that class's doc comment gives:
    /// SkiaSharp 3.119.4 cannot be asked which state to leave a wrapped resource in (see
    /// <see cref="D3D12SkiaSurfaceFactory.EndDraw(bool)"/>).
    ///
    /// Submissions are asynchronous: each call returns as soon as its command list is queued. A ring
    /// of allocator/list pairs, each tagged with a fence value, holds the in-flight submissions. When
    /// the oldest one has not finished yet, the ring grows by a pair instead of waiting, up to
    /// <see cref="MaxSlots"/>, so the CPU does not stall on the GPU in steady state however many
    /// targets the host draws per frame. <see cref="WaitForCompletion"/> drains everything, for teardown. Every COM call goes through <see cref="D3D12Com"/>'s raw
    /// vtable slots - no interop package. Not thread-safe; one instance per device/queue, driven from
    /// whatever thread the host lets submit.
    /// </summary>
    public sealed class D3D12ResourceTransitioner : IDisposable
    {
        /// <summary>The most allocator/list pairs the ring grows to before a call waits for the oldest one.</summary>
        public const int MaxSlots = 64;

        readonly IntPtr _device;
        readonly IntPtr _queue;
        readonly Func<IDisposable>? _acquireQueueLock;
        readonly List<Slot> _slots = new();
        readonly IntPtr _fence;
        readonly IntPtr _fenceEvent;
        ulong _lastSignaledValue;
        int _nextSlot;
        bool _disposed;

        struct Slot
        {
            public IntPtr Allocator;
            public IntPtr CommandList;
            public ulong FenceValue;
        }

        /// <param name="device">The host's <c>ID3D12Device*</c>.</param>
        /// <param name="queue">The host's direct <c>ID3D12CommandQueue*</c> - the same queue Skia and the host render with.</param>
        /// <param name="acquireQueueLock">The same optional queue-lock hook <see cref="D3D12SkiaSurfaceFactory.InitializeFromNative"/> takes; bracketed around each <c>ExecuteCommandLists</c>/<c>Signal</c> pair.</param>
        /// <param name="slots">How many allocator/list pairs the ring starts with. It grows past this on its own (see the class doc comment).</param>
        public D3D12ResourceTransitioner(IntPtr device, IntPtr queue, Func<IDisposable>? acquireQueueLock = null, int slots = 8)
        {
            if (device == IntPtr.Zero)
                throw new ArgumentException("D3D12 device native pointer is null.", nameof(device));
            if (queue == IntPtr.Zero)
                throw new ArgumentException("D3D12 command queue native pointer is null.", nameof(queue));
            if (slots < 1 || slots > MaxSlots)
                throw new ArgumentOutOfRangeException(nameof(slots), slots, $"Must be between 1 and {MaxSlots}.");

            _device = device;
            _queue = queue;
            _acquireQueueLock = acquireQueueLock;
            try
            {
                _fence = CreateFence(device, 0);
                _fenceEvent = CreateEvent();
                for (int i = 0; i < slots; i++)
                    _slots.Add(CreateSlot());
            }
            catch
            {
                DestroyAll();
                throw;
            }
        }

        /// <summary>Queues one transition of <paramref name="resource"/> (all subresources) from <paramref name="stateBefore"/> to <paramref name="stateAfter"/>.</summary>
        public void Transition(IntPtr resource, uint stateBefore, uint stateAfter)
        {
            if (resource == IntPtr.Zero)
                throw new ArgumentException("ID3D12Resource handle is null.", nameof(resource));

            var index = BeginRecording();
            var list = _slots[index].CommandList;
            try
            {
                Span<D3D12_RESOURCE_BARRIER> barrier = [D3D12_RESOURCE_BARRIER.Transition(resource, stateBefore, stateAfter)];
                ResourceBarrier(list, barrier);
            }
            catch
            {
                Close(list);
                throw;
            }
            Submit(index);
        }

        /// <summary>
        /// Queues <c>CopyResource(destination, source)</c>, transitioning both resources into the copy
        /// states first and back to the given resting states afterward, all in one submission. The
        /// resources must have identical dimensions and formats from the same typeless family (e.g.
        /// a typed <c>R8G8B8A8_UNORM</c> source into an <c>R8G8B8A8_TYPELESS</c> destination).
        /// </summary>
        public void CopyWithTransitions(
            IntPtr destination, uint destinationStateBefore, uint destinationStateAfter,
            IntPtr source, uint sourceStateBefore, uint sourceStateAfter)
        {
            if (destination == IntPtr.Zero)
                throw new ArgumentException("Destination ID3D12Resource handle is null.", nameof(destination));
            if (source == IntPtr.Zero)
                throw new ArgumentException("Source ID3D12Resource handle is null.", nameof(source));

            var index = BeginRecording();
            var list = _slots[index].CommandList;
            try
            {
                Span<D3D12_RESOURCE_BARRIER> before =
                [
                    D3D12_RESOURCE_BARRIER.Transition(destination, destinationStateBefore, D3D12Constants.ResourceStateCopyDest),
                    D3D12_RESOURCE_BARRIER.Transition(source, sourceStateBefore, D3D12Constants.ResourceStateCopySource),
                ];
                ResourceBarrier(list, before);
                CopyResource(list, destination, source);
                Span<D3D12_RESOURCE_BARRIER> after =
                [
                    D3D12_RESOURCE_BARRIER.Transition(destination, D3D12Constants.ResourceStateCopyDest, destinationStateAfter),
                    D3D12_RESOURCE_BARRIER.Transition(source, D3D12Constants.ResourceStateCopySource, sourceStateAfter),
                ];
                ResourceBarrier(list, after);
            }
            catch
            {
                Close(list);
                throw;
            }
            Submit(index);
        }

        /// <summary>How many allocator/list pairs the ring holds right now.</summary>
        public int SlotCount => _slots.Count;

        Slot CreateSlot()
        {
            var slot = new Slot();
            try
            {
                slot.Allocator = CreateCommandAllocator(_device);
                slot.CommandList = CreateCommandList(_device, slot.Allocator);
                // Created in the recording state; close it so every use starts with Reset.
                Close(slot.CommandList);
                return slot;
            }
            catch
            {
                DestroySlot(slot);
                throw;
            }
        }

        /// <summary>
        /// Picks the slot to record into (the oldest, unless it is still executing and the ring can
        /// grow), waits for it if it must, and leaves its command list open. The caller records, then
        /// calls <see cref="Submit"/>, or closes the list itself if recording throws, so a failed
        /// recording cannot leave the slot's list open for its next use.
        /// </summary>
        int BeginRecording()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var index = _nextSlot;
            var oldest = _slots[index];
            if (oldest.FenceValue > GetCompletedValue(_fence) && _slots.Count < MaxSlots)
                _slots.Insert(index, CreateSlot());
            _nextSlot = (index + 1) % _slots.Count;

            var slot = _slots[index];
            if (slot.FenceValue != 0)
                WaitForFenceValue(_fence, slot.FenceValue, _fenceEvent);

            ResetAllocator(slot.Allocator);
            ResetList(slot.CommandList, slot.Allocator);
            return index;
        }

        void Submit(int index)
        {
            var slot = _slots[index];
            Close(slot.CommandList);

            using (_acquireQueueLock?.Invoke())
            {
                ExecuteCommandList(_queue, slot.CommandList);
                // Recorded only once Signal succeeds: a fence value that is never signaled would make
                // the next wait on this slot, or WaitForCompletion, block forever.
                var value = _lastSignaledValue + 1;
                Signal(_queue, _fence, value);
                _lastSignaledValue = value;
                slot.FenceValue = value;
            }
            _slots[index] = slot;
        }

        /// <summary>Blocks until every queued submission has executed on the GPU (no-op if none is pending).</summary>
        public void WaitForCompletion()
        {
            if (_lastSignaledValue == 0)
                return;
            WaitForFenceValue(_fence, _lastSignaledValue, _fenceEvent);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                WaitForCompletion();
            }
            finally
            {
                DestroyAll();
            }
        }

        void DestroyAll()
        {
            foreach (var slot in _slots)
                DestroySlot(slot);
            _slots.Clear();
            if (_fence != IntPtr.Zero)
                Release(_fence);
            CloseEvent(_fenceEvent);
        }

        static void DestroySlot(Slot slot)
        {
            if (slot.CommandList != IntPtr.Zero)
                Release(slot.CommandList);
            if (slot.Allocator != IntPtr.Zero)
                Release(slot.Allocator);
        }
    }
}
