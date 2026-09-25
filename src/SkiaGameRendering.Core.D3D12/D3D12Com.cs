using System.Runtime.InteropServices;

namespace SkiaGameRendering.Core.D3D12
{
    /// <summary>
    /// Raw COM vtable calls for the D3D12 plumbing Core.D3D12 owns itself: creating a typed
    /// render-target resource, and recording/submitting the barriers and copies
    /// <see cref="D3D12ResourceTransitioner"/> issues. Same discipline as Core.ANGLE's
    /// <c>D3D11Com</c>: every slot index follows the interface's declaration order in d3d12.h
    /// (IUnknown 0-2, ID3D12Object 3-6, ID3D12DeviceChild 7, then the interface's own methods),
    /// cross-checked against <c>tests/Tests.Core.D3D12/D3D12TestNative.cs</c>, whose slots were
    /// verified against terrafx.interop.windows rather than counted by eye. An off-by-one calls a
    /// neighboring method with the wrong signature, so treat the numbers as load-bearing.
    /// </summary>
    internal static unsafe class D3D12Com
    {
        internal static readonly Guid IID_ID3D12Resource = new("696442be-a72e-4059-bc79-5b5c98040fad");
        internal static readonly Guid IID_ID3D12CommandAllocator = new("6102dee4-af59-4b09-b999-b44d73f09b24");
        internal static readonly Guid IID_ID3D12GraphicsCommandList = new("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455");
        internal static readonly Guid IID_ID3D12Fence = new("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");

        internal const int D3D12_COMMAND_LIST_TYPE_DIRECT = 0;
        internal const int D3D12_FENCE_FLAG_NONE = 0;
        internal const int D3D12_HEAP_TYPE_DEFAULT = 1;
        internal const uint D3D12_HEAP_FLAG_NONE = 0;
        internal const int D3D12_RESOURCE_DIMENSION_TEXTURE2D = 3;
        internal const int D3D12_TEXTURE_LAYOUT_UNKNOWN = 0;
        internal const uint D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET = 0x1;
        internal const int D3D12_RESOURCE_BARRIER_TYPE_TRANSITION = 0;
        internal const uint D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES = 0xFFFFFFFF;
        internal const int D3D12_FEATURE_D3D12_OPTIONS12 = 41;
        internal const uint INFINITE = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential)]
        internal struct D3D12_HEAP_PROPERTIES
        {
            internal int Type;
            internal int CPUPageProperty;
            internal int MemoryPoolPreference;
            internal uint CreationNodeMask;
            internal uint VisibleNodeMask;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DXGI_SAMPLE_DESC
        {
            internal uint Count;
            internal uint Quality;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct D3D12_RESOURCE_DESC
        {
            internal int Dimension;
            internal ulong Alignment;
            internal ulong Width;
            internal uint Height;
            internal ushort DepthOrArraySize;
            internal ushort MipLevels;
            internal uint Format;
            internal DXGI_SAMPLE_DESC SampleDesc;
            internal int Layout;
            internal uint Flags;
        }

        /// <summary>Flattened transition-variant <c>D3D12_RESOURCE_BARRIER</c>; offsets as verified in the test harness.</summary>
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        internal struct D3D12_RESOURCE_BARRIER
        {
            [FieldOffset(0)] internal int Type;
            [FieldOffset(4)] internal int Flags;
            [FieldOffset(8)] internal IntPtr TransitionResource;
            [FieldOffset(16)] internal uint TransitionSubresource;
            [FieldOffset(20)] internal uint TransitionStateBefore;
            [FieldOffset(24)] internal uint TransitionStateAfter;

            internal static D3D12_RESOURCE_BARRIER Transition(IntPtr resource, uint before, uint after) => new()
            {
                Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION,
                Flags = 0,
                TransitionResource = resource,
                TransitionSubresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,
                TransitionStateBefore = before,
                TransitionStateAfter = after,
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct D3D12_FEATURE_DATA_D3D12_OPTIONS12
        {
            internal int MSPrimitivesPipelineStatisticIncludesCulledPrimitives;
            internal int EnhancedBarriersSupported;
            internal int RelaxedFormatCastingSupported;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEventW(IntPtr lpEventAttributes, [MarshalAs(UnmanagedType.Bool)] bool bManualReset, [MarshalAs(UnmanagedType.Bool)] bool bInitialState, IntPtr lpName);

        [DllImport("kernel32.dll")]
        static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        static void Check(int hr, string call)
        {
            if (hr < 0)
                throw new InvalidOperationException($"{call} failed. HRESULT: 0x{hr:X8}");
        }

        /// <summary>IUnknown::Release, vtable slot 2.</summary>
        internal static uint Release(IntPtr unknown)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint>)(*(void***)unknown)[2];
            return fn(unknown);
        }

        /// <summary>ID3D12Device::CreateCommandAllocator, vtable slot 9.</summary>
        internal static IntPtr CreateCommandAllocator(IntPtr device)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, int, Guid*, void**, int>)(*(void***)device)[9];
            void* result;
            fixed (Guid* iid = &IID_ID3D12CommandAllocator)
                Check(fn(device, D3D12_COMMAND_LIST_TYPE_DIRECT, iid, &result), "ID3D12Device::CreateCommandAllocator");
            return (IntPtr)result;
        }

        /// <summary>ID3D12Device::CreateCommandList, vtable slot 12. The list is created in the recording state.</summary>
        internal static IntPtr CreateCommandList(IntPtr device, IntPtr allocator)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, int, IntPtr, IntPtr, Guid*, void**, int>)(*(void***)device)[12];
            void* result;
            fixed (Guid* iid = &IID_ID3D12GraphicsCommandList)
                Check(fn(device, 0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator, IntPtr.Zero, iid, &result), "ID3D12Device::CreateCommandList");
            return (IntPtr)result;
        }

        /// <summary>ID3D12Device::CheckFeatureSupport, vtable slot 13, for <c>D3D12_FEATURE_D3D12_OPTIONS12.EnhancedBarriersSupported</c>.</summary>
        internal static bool CheckEnhancedBarriersSupported(IntPtr device)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, int, void*, uint, int>)(*(void***)device)[13];
            var options = new D3D12_FEATURE_DATA_D3D12_OPTIONS12();
            int hr = fn(device, D3D12_FEATURE_D3D12_OPTIONS12, &options, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS12));
            // An older runtime that does not know OPTIONS12 fails the query; that runtime has no
            // enhanced barriers either, which is the same answer.
            return hr >= 0 && options.EnhancedBarriersSupported != 0;
        }

        /// <summary>ID3D12Device::CreateCommittedResource, vtable slot 27.</summary>
        internal static IntPtr CreateCommittedResource(IntPtr device, in D3D12_HEAP_PROPERTIES heap, in D3D12_RESOURCE_DESC desc, uint initialState)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, D3D12_HEAP_PROPERTIES*, uint, D3D12_RESOURCE_DESC*, uint, IntPtr, Guid*, void**, int>)(*(void***)device)[27];
            void* result;
            fixed (D3D12_HEAP_PROPERTIES* heapPtr = &heap)
            fixed (D3D12_RESOURCE_DESC* descPtr = &desc)
            fixed (Guid* iid = &IID_ID3D12Resource)
                Check(fn(device, heapPtr, D3D12_HEAP_FLAG_NONE, descPtr, initialState, IntPtr.Zero, iid, &result), "ID3D12Device::CreateCommittedResource");
            return (IntPtr)result;
        }

        /// <summary>ID3D12Device::CreateFence, vtable slot 36.</summary>
        internal static IntPtr CreateFence(IntPtr device, ulong initialValue)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, ulong, int, Guid*, void**, int>)(*(void***)device)[36];
            void* result;
            fixed (Guid* iid = &IID_ID3D12Fence)
                Check(fn(device, initialValue, D3D12_FENCE_FLAG_NONE, iid, &result), "ID3D12Device::CreateFence");
            return (IntPtr)result;
        }

        /// <summary>ID3D12CommandAllocator::Reset, vtable slot 8.</summary>
        internal static void ResetAllocator(IntPtr allocator)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, int>)(*(void***)allocator)[8];
            Check(fn(allocator), "ID3D12CommandAllocator::Reset");
        }

        /// <summary>ID3D12GraphicsCommandList::Close, vtable slot 9.</summary>
        internal static void Close(IntPtr list)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, int>)(*(void***)list)[9];
            Check(fn(list), "ID3D12GraphicsCommandList::Close");
        }

        /// <summary>ID3D12GraphicsCommandList::Reset, vtable slot 10.</summary>
        internal static void ResetList(IntPtr list, IntPtr allocator)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, IntPtr, int>)(*(void***)list)[10];
            Check(fn(list, allocator, IntPtr.Zero), "ID3D12GraphicsCommandList::Reset");
        }

        /// <summary>ID3D12GraphicsCommandList::CopyResource, vtable slot 17.</summary>
        internal static void CopyResource(IntPtr list, IntPtr destination, IntPtr source)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, IntPtr, void>)(*(void***)list)[17];
            fn(list, destination, source);
        }

        /// <summary>ID3D12GraphicsCommandList::ResourceBarrier, vtable slot 26.</summary>
        internal static void ResourceBarrier(IntPtr list, ReadOnlySpan<D3D12_RESOURCE_BARRIER> barriers)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, D3D12_RESOURCE_BARRIER*, void>)(*(void***)list)[26];
            fixed (D3D12_RESOURCE_BARRIER* ptr = barriers)
                fn(list, (uint)barriers.Length, ptr);
        }

        /// <summary>ID3D12CommandQueue::ExecuteCommandLists, vtable slot 10.</summary>
        internal static void ExecuteCommandList(IntPtr queue, IntPtr list)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, uint, IntPtr*, void>)(*(void***)queue)[10];
            fn(queue, 1, &list);
        }

        /// <summary>ID3D12CommandQueue::Signal, vtable slot 14.</summary>
        internal static void Signal(IntPtr queue, IntPtr fence, ulong value)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, IntPtr, ulong, int>)(*(void***)queue)[14];
            Check(fn(queue, fence, value), "ID3D12CommandQueue::Signal");
        }

        /// <summary>ID3D12Fence::GetCompletedValue, vtable slot 8.</summary>
        internal static ulong GetCompletedValue(IntPtr fence)
        {
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, ulong>)(*(void***)fence)[8];
            return fn(fence);
        }

        /// <summary>Blocks until <paramref name="fence"/> reaches <paramref name="value"/>, via ID3D12Fence::SetEventOnCompletion (vtable slot 9) and a Win32 event.</summary>
        internal static void WaitForFenceValue(IntPtr fence, ulong value, IntPtr eventHandle)
        {
            if (GetCompletedValue(fence) >= value)
                return;
            var fn = (delegate* unmanaged[MemberFunction]<IntPtr, ulong, IntPtr, int>)(*(void***)fence)[9];
            Check(fn(fence, value, eventHandle), "ID3D12Fence::SetEventOnCompletion");
            WaitForSingleObject(eventHandle, INFINITE);
        }

        internal static IntPtr CreateEvent()
        {
            var handle = CreateEventW(IntPtr.Zero, bManualReset: false, bInitialState: false, IntPtr.Zero);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("CreateEventW failed. Win32 error: " + Marshal.GetLastWin32Error());
            return handle;
        }

        internal static void CloseEvent(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
                CloseHandle(handle);
        }
    }
}
