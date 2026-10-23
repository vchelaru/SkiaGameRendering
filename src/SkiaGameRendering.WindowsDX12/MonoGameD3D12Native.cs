using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.D3D12;

namespace SkiaGameRendering
{
    /// <summary>
    /// Reads MonoGame's native D3D12 backend (<c>mgruntime.dll</c>) from outside, so a <see cref="SkiaDirectCanvas"/>
    /// pass can find the target MonoGame is drawing to and cut MonoGame's command list in two around Skia's work.
    /// MonoGame exposes neither the <c>ID3D12Resource</c> behind its current target nor any way to submit what it
    /// has recorded, so this walks its C++ objects by offset. That is only safe against a build whose layout has
    /// been checked, so <see cref="TryCreate"/> refuses any <c>mgruntime.dll</c> not in <see cref="KnownRuntimes"/>.
    ///
    /// MAINTENANCE NOTES:
    /// - <b>Where the layout comes from.</b> The structs are in MonoGame/MonoGame at <c>native/monogame/directx12</c>
    ///   (<c>MGG_DX12.cpp</c>'s <c>MGG_GraphicsDevice</c>, <c>CommandContext.h</c>, <c>Heaps.h</c>, <c>Texture.h</c>'s
    ///   <c>InternalData</c>, <c>PipelineState.h</c>'s <c>InternalData</c>), x64 MSVC release layout. Every offset below
    ///   was then confirmed against the shipped DLL by probing a live device: the pointer chains by following them,
    ///   the dirty flags by diffing <c>MGG_GraphicsDevice</c> across a Present (which sets them all), the pipeline hash
    ///   by diffing its object across draws with different pipeline state. Comments name the field each one is.
    /// - <b>Bumping MonoGame.</b> 3.8.6-preview.2 and 3.8.6 ship the same native code (the two x64 DLLs differ in 28
    ///   bytes: the PE timestamp and debug GUID). For any other build, diff the structs against the source at that tag,
    ///   re-probe, add the DLL's SHA-256 to <see cref="KnownRuntimes"/>. Until then <see cref="SkiaRenderer.SupportsDirectCanvas"/>
    ///   is false on that build and the render-target path keeps working.
    /// - <b>What a cut has to put back.</b> <c>CommandContext::Reset</c> binds the root signature and descriptor heaps;
    ///   <c>SetRenderTarget</c> binds the targets; the rest of the list state is applied lazily from dirty flags in
    ///   <c>MGDX_ApplyState</c> and from a pipeline-state hash, so raising those flags (<see cref="MarkStateDirty"/>)
    ///   makes the next draw re-record it. The blend factor and stencil reference are recorded straight onto the list
    ///   by MonoGame's managed state setters, so the matching managed dirty flags are raised too.
    /// </summary>
    internal sealed unsafe class MonoGameD3D12Native
    {
        // SHA-256 of the win-x64 mgruntime.dll in MonoGame.Runtime.Windows.DX12 at each verified version.
        static readonly Dictionary<string, string> KnownRuntimes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["234ba4b1a9ce5adc24b34432c10727db55ff9c902e3fde59e87ae56e60e81e36"] = "3.8.6-preview.2",
            ["1179b04b0aa1f983dd50e37f03d797040e35e9a3a13a50b34d7a776cf0de8ab0"] = "3.8.6",
        };

        // MGG_GraphicsDevice (MGG_DX12.cpp). Offsets in bytes; FrameCounter is a 32-bit unsigned.
        const int DeviceFrame = 0;                // std::atomic<FrameCounter> frame: presented frames
        const int DeviceIsRecording = 8;          // bool is_recording
        const int DeviceResources = 24;           // DeviceResources* resources
        const int DeviceContext = 32;             // CommandContext* context
        const int DevicePipelineManager = 40;     // PipelineStateManager* pipelineManager
        const int DeviceUniformsDirty = 96;       // uint32_t uniformsDirty
        const int DeviceVertexBuffersDirty = 104; // uint64_t vertexBuffersDirty
        const int DeviceIndexBufferDirty = 396;   // bool indexBufferDirty
        const int DeviceTexturesDirty = 656;      // bool texturesDirty
        const int DeviceSamplersDirty = 952;      // bool samplersDirty
        const int DeviceViewportDirty = 980;      // bool viewportDirty
        const int DeviceScissorDirty = 1000;      // bool scissorDirty

        // DeviceResources is a single pointer to its Impl; Impl's first member is the ComPtr<ID3D12Device>.
        // CommandContext (CommandContext.h).
        const int ContextDeviceResources = 0;     // DeviceResources* m_deviceRes
        const int ContextHeaps = 8;               // Heaps* m_heaps
        const int ContextCommandList = 24;        // ID3D12GraphicsCommandList* cmdList (null between Present and the next Prepare)
        const int ContextRootSignature = 32;      // ComPtr<ID3D12RootSignature> m_rootSig
        const int ContextRenderTargetViews = 224; // std::vector<D3D12_CPU_DESCRIPTOR_HANDLE> m_currentRTV (first, last, end)
        const int ContextRenderTargets = 248;     // std::vector<Texture*> m_currentRT
        const int ContextDepthStencil = 272;      // Texture* m_currentDepthStencil

        // Heaps (Heaps.h): m_idx, then the per-frame shader-visible heaps, each a unique_ptr to an object whose
        // second member is the ComPtr<ID3D12DescriptorHeap>.
        const int HeapsIndex = 8;                 // unsigned int m_idx
        const int HeapsResourceHeaps = 96;        // unique_ptr<ShaderVisibleDescHeap> m_srvShaderHeap[MAX_BACK_BUFFER_COUNT]
        const int HeapsSamplerHeaps = 136;        // unique_ptr<ShaderVisibleDescHeap> m_samplerShaderHeap[MAX_BACK_BUFFER_COUNT]
        const int MaxBackBufferCount = 3;
        const int DescriptorHeapObject = 8;       // NonShaderVisibleDescHeap::m_heap

        // PipelineStateManager::InternalData (PipelineState.h): m_deviceRes, a 656-byte D3D12_GRAPHICS_PIPELINE_STATE_DESC,
        // a std::vector, then the hash of the last pipeline state set on the list.
        const int PipelineManagerImpl = 0;
        const int PipelineLastHash = 688;

        // Texture::InternalData (Texture.h). The pointer a Texture holds is that struct's address.
        const int TextureState = 72;              // D3D12_RESOURCE_STATES m_currentState: what MonoGame believes the resource is in
        const int TextureResource = 112;          // ComPtr<ID3D12Resource> m_res
        const int TextureDepthView = 176;         // D3D12_CPU_DESCRIPTOR_HANDLE m_dsvHandle

        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly string[] ManagedDirtyFlags = ["_blendStateDirty", "_blendFactorDirty", "_depthStencilStateDirty"];

        static FieldInfo? _deviceHandle;
        static FieldInfo[]? _managedFlags;

        readonly GraphicsDevice _graphicsDevice;
        readonly byte* _device;

        MonoGameD3D12Native(GraphicsDevice graphicsDevice, byte* device)
        {
            _graphicsDevice = graphicsDevice;
            _device = device;
        }

        // Read fresh each time: MonoGame may replace these objects when it resets the device.
        byte* Context => *(byte**)(_device + DeviceContext);

        byte* PipelineImpl => *(byte**)(*(byte**)(_device + DevicePipelineManager) + PipelineManagerImpl);

        /// <summary>
        /// Checks that the loaded <c>mgruntime.dll</c> is a verified build and that its objects have the shape this
        /// class expects. Returns null and the reason, never throws, when they do not.
        /// </summary>
        [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(GraphicsDevice))]
        internal static MonoGameD3D12Native? TryCreate(GraphicsDevice graphicsDevice, NativeGraphicsHandles handles, out string? reason)
        {
            try
            {
                var runtime = FindRuntime();
                if (runtime == null)
                {
                    reason = "mgruntime.dll is not loaded.";
                    return null;
                }
                if (!KnownRuntimes.ContainsKey(runtime.Value.hash))
                {
                    reason = $"{runtime.Value.path} (SHA-256 {runtime.Value.hash}) is not a MonoGame build whose native layout " +
                             $"has been verified; verified: {string.Join(", ", KnownRuntimes.Values)}.";
                    return null;
                }

                _deviceHandle ??= typeof(GraphicsDevice).GetField("Handle", Instance)
                    ?? throw new MissingMemberException("GraphicsDevice.Handle was not found; MonoGame's internal layout changed.");
                var device = (byte*)Pointer.Unbox(_deviceHandle.GetValue(graphicsDevice)!);
                if (device == null)
                {
                    reason = "The GraphicsDevice has no native device.";
                    return null;
                }

                var resources = *(byte**)(device + DeviceResources);
                var context = *(byte**)(device + DeviceContext);
                var pipelineManager = *(byte**)(device + DevicePipelineManager);
                if (resources == null || context == null || pipelineManager == null)
                {
                    reason = "MonoGame's native device does not have the expected objects.";
                    return null;
                }

                // DeviceResources -> Impl -> m_d3dDevice is the device GetNativeHandles reports.
                var resourcesImpl = *(byte**)resources;
                if (resourcesImpl == null || *(byte**)resourcesImpl != (byte*)handles.LogicalDevice)
                {
                    reason = "MonoGame's native DeviceResources does not hold the device GetNativeHandles reports.";
                    return null;
                }
                if (*(byte**)(context + ContextDeviceResources) != resources)
                {
                    reason = "MonoGame's native CommandContext does not point back at the DeviceResources.";
                    return null;
                }
                var pipelineImpl = *(byte**)(pipelineManager + PipelineManagerImpl);
                if (pipelineImpl == null || *(byte**)pipelineImpl != resources)
                {
                    reason = "MonoGame's native PipelineStateManager does not point at the DeviceResources.";
                    return null;
                }
                if (*(byte**)(context + ContextRootSignature) == null)
                {
                    reason = "MonoGame's native CommandContext has no root signature.";
                    return null;
                }

                var heaps = *(byte**)(context + ContextHeaps);
                if (heaps == null || *(uint*)(heaps + HeapsIndex) >= MaxBackBufferCount ||
                    ResourceHeap(heaps) == IntPtr.Zero || SamplerHeap(heaps) == IntPtr.Zero)
                {
                    reason = "MonoGame's native Heaps does not have the expected descriptor heaps.";
                    return null;
                }

                _managedFlags ??= ManagedDirtyFlags
                    .Select(name => typeof(GraphicsDevice).GetField(name, Instance)
                        ?? throw new MissingMemberException($"GraphicsDevice.{name} was not found; MonoGame's internal layout changed."))
                    .ToArray();

                reason = null;
                return new MonoGameD3D12Native(graphicsDevice, device);
            }
            catch (Exception exception) when (exception is MissingMemberException or IOException or UnauthorizedAccessException)
            {
                reason = exception.Message;
                return null;
            }
        }

        /// <summary>The MonoGame version whose <c>mgruntime.dll</c> has this SHA-256 and a verified layout, or null.</summary>
        internal static string? VerifiedVersion(string sha256) =>
            KnownRuntimes.TryGetValue(sha256, out var version) ? version : null;

        /// <summary>The SHA-256 (upper-case hex) of the file at <paramref name="path"/>.</summary>
        internal static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        /// <summary>True between MonoGame's Prepare and Present, when it has an open command list.</summary>
        internal bool IsRecording => _device[DeviceIsRecording] != 0 && CommandList != IntPtr.Zero;

        /// <summary>MonoGame's open <c>ID3D12GraphicsCommandList*</c>, or zero between frames.</summary>
        internal IntPtr CommandList => (IntPtr)(*(byte**)(Context + ContextCommandList));

        /// <summary>The count of presented frames, which only goes up.</summary>
        internal long Frame => *(uint*)(_device + DeviceFrame);

        /// <summary>
        /// The one resource MonoGame is rendering to now, and the state MonoGame will assume it is in. Throws if
        /// there are no targets bound or several.
        /// </summary>
        internal (IntPtr resource, uint state) CurrentTarget()
        {
            var targets = *(byte***)(Context + ContextRenderTargets);
            var end = *(byte***)(Context + ContextRenderTargets + sizeof(void*));
            var count = end - targets;
            if (count == 0)
                throw new InvalidOperationException(
                    "MonoGame has no render target bound yet this frame. Clear the target or call SetRenderTarget before SkiaDirectCanvas.Begin.");
            if (count != 1)
                throw new NotSupportedException($"SkiaDirectCanvas draws into a single render target, not {count}.");

            var data = TextureData(targets[0]);
            return ((IntPtr)(*(byte**)(data + TextureResource)), *(uint*)(data + TextureState));
        }

        /// <summary>Puts back what <c>CommandContext::Reset</c> and <c>SetRenderTarget</c> bind on a list that was just reopened.</summary>
        internal void BindListState()
        {
            var list = CommandList;
            var heaps = *(byte**)(Context + ContextHeaps);
            D3D12CommandListSplitter.SetGraphicsRootSignature(list, (IntPtr)(*(byte**)(Context + ContextRootSignature)));
            D3D12CommandListSplitter.SetDescriptorHeaps(list, ResourceHeap(heaps), SamplerHeap(heaps));

            var views = *(nuint**)(Context + ContextRenderTargetViews);
            var viewsEnd = *(nuint**)(Context + ContextRenderTargetViews + sizeof(void*));
            var depth = *(byte**)(Context + ContextDepthStencil);
            D3D12CommandListSplitter.SetRenderTargets(list,
                new ReadOnlySpan<nuint>(views, (int)(viewsEnd - views)),
                depth == null ? 0 : *(nuint*)(TextureData(depth) + TextureDepthView));
        }

        /// <summary>
        /// Makes MonoGame re-record every piece of list state its draws rely on: the vertex and index buffers, constant
        /// buffers, texture and sampler tables, viewport and scissor, the pipeline state, and the blend factor and
        /// stencil reference. Needed after <see cref="D3D12CommandListSplitter.Reopen"/>, which leaves the list empty.
        /// </summary>
        internal void MarkStateDirty()
        {
            *(uint*)(_device + DeviceUniformsDirty) = 0xFFFFFFFF;
            *(ulong*)(_device + DeviceVertexBuffersDirty) = 0xFFFFFFFF;
            _device[DeviceIndexBufferDirty] = 1;
            _device[DeviceTexturesDirty] = 1;
            _device[DeviceSamplersDirty] = 1;
            _device[DeviceViewportDirty] = 1;
            _device[DeviceScissorDirty] = 1;
            *(ulong*)(PipelineImpl + PipelineLastHash) = 0;

            foreach (var flag in _managedFlags!)
                flag.SetValue(_graphicsDevice, true);
        }

        static byte* TextureData(byte* texture) => *(byte**)texture;

        static IntPtr ResourceHeap(byte* heaps) => Heap(heaps, HeapsResourceHeaps);

        static IntPtr SamplerHeap(byte* heaps) => Heap(heaps, HeapsSamplerHeaps);

        static IntPtr Heap(byte* heaps, int arrayOffset)
        {
            var holder = *(byte**)(heaps + arrayOffset + (*(uint*)(heaps + HeapsIndex)) * sizeof(void*));
            return holder == null ? IntPtr.Zero : (IntPtr)(*(byte**)(holder + DescriptorHeapObject));
        }

        // --- the loaded runtime ---

        static (string path, string hash)? FindRuntime()
        {
            var module = GetModuleHandleW("mgruntime");
            if (module == IntPtr.Zero)
                return null;

            var buffer = new char[1024];
            uint length;
            fixed (char* chars = buffer)
                length = GetModuleFileNameW(module, chars, (uint)buffer.Length);
            if (length == 0 || length >= buffer.Length)
                return null;

            var path = new string(buffer, 0, (int)length);
            return (path, HashFile(path));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern uint GetModuleFileNameW(IntPtr module, char* fileName, uint size);
    }
}
