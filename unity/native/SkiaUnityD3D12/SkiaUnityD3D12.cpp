// Hands Unity's D3D12 device, command queue and adapter to SkiaGameRendering.Unity, which gives them
// to Core.D3D12, and puts a texture in the state Skia draws it in. Unity's C# API exposes none of
// this. Built by eng/build-unity-d3d12-plugin.ps1; the output is committed.
//
// The interface struct is declared here, not included from Unity's PluginAPI headers
// (IUnityInterface.h, IUnityGraphicsD3D12.h), so the build doesn't need a Unity install. Only the
// members used are typed; recheck the layout against those headers after a Unity upgrade.

#include <d3d12.h>
#include <dxgi1_4.h>

struct IUnityInterfaces
{
    void* GetInterface;
    void* RegisterInterface;
    void* (__stdcall* GetInterfaceSplit)(unsigned long long guidHigh, unsigned long long guidLow);
    void* RegisterInterfaceSplit;
};

struct UnityGraphicsD3D12ResourceState
{
    ID3D12Resource* resource;
    D3D12_RESOURCE_STATES expected; // State before the command list runs; Unity barriers the resource into it.
    D3D12_RESOURCE_STATES current;  // State after; Unity tracks the resource as being in it.
};

struct UnityD3D12PluginEventConfig
{
    int graphicsQueueAccess;     // kUnityD3D12GraphicsQueueAccess_Allow (1): the event runs on the submission thread, where the queue is safe to use.
    unsigned int flags;          // kUnityD3D12EventConfigFlag_FlushCommandBuffers (2): Unity submits what it has recorded before the event.
    bool ensureActiveRenderTextureIsBound;
};

// IUnityGraphicsD3D12v7's leading members, in header order.
struct IUnityGraphicsD3D12v7
{
    ID3D12Device* (__stdcall* GetDevice)();
    void* GetSwapChain;
    void* GetSyncInterval;
    void* GetPresentFlags;
    void* GetFrameFence;
    void* GetNextFrameFenceValue;
    unsigned long long (__stdcall* ExecuteCommandList)(ID3D12GraphicsCommandList* commandList, int stateCount, UnityGraphicsD3D12ResourceState* states);
    void* SetPhysicalVideoMemoryControlValues;
    ID3D12CommandQueue* (__stdcall* GetCommandQueue)();
    ID3D12Resource* (__stdcall* TextureFromRenderBuffer)(void* renderBuffer);
    void* TextureFromNativeTexture;
    void (__stdcall* ConfigureEvent)(int eventId, const UnityD3D12PluginEventConfig* config);
};

static const unsigned long long D3D12V7GuidHigh = 0x4624B0DA41B64AACULL;
static const unsigned long long D3D12V7GuidLow = 0x915AABCB9BC3F0D3ULL;

static IUnityGraphicsD3D12v7* s_d3d12;
static const int DirectEventId = 0x534B03;
static const int DrawEventId = 0x534B04;
static IDXGIAdapter1* s_adapter;
static ID3D12CommandAllocator* s_allocator;
static ID3D12GraphicsCommandList* s_emptyList;

static void Cleanup()
{
    if (s_emptyList) { s_emptyList->Release(); s_emptyList = nullptr; }
    if (s_allocator) { s_allocator->Release(); s_allocator = nullptr; }
    if (s_adapter) { s_adapter->Release(); s_adapter = nullptr; }
}

extern "C" __declspec(dllexport) void __stdcall UnityPluginLoad(IUnityInterfaces* interfaces)
{
    s_d3d12 = (IUnityGraphicsD3D12v7*)interfaces->GetInterfaceSplit(D3D12V7GuidHigh, D3D12V7GuidLow);
    if (s_d3d12)
    {
        // SkiaDirectCanvas draws on top of what Unity has recorded so far, and submits straight to Unity's
        // queue, so Unity must first submit its recorded command lists (flag 2) and run the event where
        // the queue can be used (access 1), with the camera's target still bound (it is read afterward).
        UnityD3D12PluginEventConfig config = { 1, 2, true };
        s_d3d12->ConfigureEvent(DirectEventId, &config);
        // A SkiaRenderTarget2D draw, and the releases queued with it, need the same. On the default config the event runs
        // on the render thread, where the queue is off limits and Skia's submit can pass Unity's own still-unsubmitted
        // work (such as a new texture's first-use clear), which then wipes what Skia drew. Nothing reads the bound
        // target afterward, so it isn't bound.
        UnityD3D12PluginEventConfig drawConfig = { 1, 2, false };
        s_d3d12->ConfigureEvent(DrawEventId, &drawConfig);
    }
}

extern "C" __declspec(dllexport) void __stdcall UnityPluginUnload()
{
    Cleanup();
    s_d3d12 = nullptr;
}

// Null when Unity isn't running on D3D12 or never called UnityPluginLoad.
extern "C" __declspec(dllexport) ID3D12Device* SkiaUnityD3D12_Device()
{
    return s_d3d12 ? s_d3d12->GetDevice() : nullptr;
}

extern "C" __declspec(dllexport) ID3D12CommandQueue* SkiaUnityD3D12_CommandQueue()
{
    return s_d3d12 ? s_d3d12->GetCommandQueue() : nullptr;
}

// The IDXGIAdapter1 Unity's device was created on, which Skia's D3D backend needs and Unity doesn't
// hand out. Owned by the plugin; don't release it.
extern "C" __declspec(dllexport) IDXGIAdapter1* SkiaUnityD3D12_Adapter()
{
    if (s_adapter || !s_d3d12)
        return s_adapter;
    ID3D12Device* device = s_d3d12->GetDevice();
    if (!device)
        return nullptr;
    IDXGIFactory4* factory = nullptr;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))))
        return nullptr;
    factory->EnumAdapterByLuid(device->GetAdapterLuid(), IID_PPV_ARGS(&s_adapter));
    factory->Release();
    return s_adapter;
}

// Render thread only. The ID3D12Resource behind a UnityRenderBuffer, which the main thread reads from
// Graphics.activeColorBuffer.GetNativeRenderBufferPtr() to name the target Unity is rendering to. Null if
// Unity doesn't know the buffer. Owned by Unity; don't release it.
extern "C" __declspec(dllexport) ID3D12Resource* SkiaUnityD3D12_ResourceFromRenderBuffer(void* renderBuffer)
{
    return s_d3d12 && renderBuffer ? s_d3d12->TextureFromRenderBuffer(renderBuffer) : nullptr;
}

// Writes the parts of the resource's descriptor that decide whether Skia can draw into it, since the
// managed side has no COM access: isTexture2D, allowsRenderTarget, sampleCount, depthOrArraySize,
// format, width, height.
extern "C" __declspec(dllexport) bool SkiaUnityD3D12_DescribeResource(ID3D12Resource* resource, unsigned long long* out7)
{
    if (!resource)
        return false;
    D3D12_RESOURCE_DESC desc = resource->GetDesc();
    out7[0] = desc.Dimension == D3D12_RESOURCE_DIMENSION_TEXTURE2D ? 1 : 0;
    out7[1] = (desc.Flags & D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET) ? 1 : 0;
    out7[2] = desc.SampleDesc.Count;
    out7[3] = desc.DepthOrArraySize;
    out7[4] = desc.Format;
    out7[5] = desc.Width;
    out7[6] = desc.Height;
    return true;
}

// Render thread only. Queues, on Unity's command queue, a barrier into RENDER_TARGET for the
// resource: Unity records its own pending work and the transition from whatever state it last
// left the resource in, then runs an empty command list of ours that declares the resource in
// RENDER_TARGET. Skia's commands submitted to the same queue afterward run after both, and Unity
// tracks the resource as RENDER_TARGET until it next uses it. Returns false on failure.
extern "C" __declspec(dllexport) bool SkiaUnityD3D12_PrepareForDrawing(ID3D12Resource* resource)
{
    if (!s_d3d12 || !resource)
        return false;
    if (!s_emptyList)
    {
        ID3D12Device* device = s_d3d12->GetDevice();
        if (!device
            || FAILED(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&s_allocator)))
            || FAILED(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, s_allocator, nullptr, IID_PPV_ARGS(&s_emptyList)))
            || FAILED(s_emptyList->Close()))
        {
            Cleanup();
            return false;
        }
    }
    UnityGraphicsD3D12ResourceState state = { resource, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_RENDER_TARGET };
    s_d3d12->ExecuteCommandList(s_emptyList, 1, &state);
    return true;
}
