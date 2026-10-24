// Hands Unity's Vulkan instance, physical device, device and graphics queue to SkiaGameRendering.Unity,
// which gives them to Core.VK, and makes Unity put a texture in the layout Skia draws it in. Unity's C#
// API exposes none of this. Built by eng/build-unity-vulkan-plugin.ps1; the output is committed.
//
// The interface structs are declared here, not included from Unity's PluginAPI headers
// (IUnityInterface.h, IUnityGraphicsVulkan.h), so the build needs neither a Unity install nor the
// Vulkan SDK. Only the members used are typed; recheck the layouts against those headers after a
// Unity upgrade.

#include <stdint.h>

struct IUnityInterfaces
{
    void* GetInterface;
    void* RegisterInterface;
    void* (__stdcall* GetInterfaceSplit)(unsigned long long guidHigh, unsigned long long guidLow);
    void* RegisterInterfaceSplit;
};

struct UnityVulkanInstance
{
    uint64_t pipelineCache;
    void* instance;
    void* physicalDevice;
    void* device;
    void* graphicsQueue;
    void* getInstanceProcAddr;
    unsigned int queueFamilyIndex;
    void* reserved[8];
};

struct UnityVulkanMemory
{
    uint64_t memory;
    uint64_t offset;
    uint64_t size;
    void* mapped;
    uint32_t flags;
    uint32_t memoryTypeIndex;
    void* reserved[4];
};

struct UnityVulkanImage
{
    UnityVulkanMemory memory;
    uint64_t image;
    int32_t layout;
    uint32_t aspect;
    uint32_t usage;
    int32_t format;
    uint32_t extent[3];
    int32_t tiling;
    int32_t type;
    int32_t samples;
    int32_t layers;
    int32_t mipCount;
    void* reserved[4];
};

struct UnityVulkanPluginEventConfig
{
    int32_t renderPassPrecondition; // 0 DontCare, 1 EnsureInside, 2 EnsureOutside
    int32_t graphicsQueueAccess;    // 0 DontCare, 1 Allow
    uint32_t flags;                 // bit 0 EnsurePreviousFrameSubmission, bit 1 FlushCommandBuffers
};

// IUnityGraphicsVulkan's members, in header order.
struct IUnityGraphicsVulkan
{
    void* InterceptInitialization;
    void* InterceptVulkanAPI;
    void (__stdcall* ConfigureEvent)(int eventId, const UnityVulkanPluginEventConfig* config);
    UnityVulkanInstance (__stdcall* Instance)();
    void* CommandRecordingState;
    bool (__stdcall* AccessTexture)(void* nativeTexture, const void* subResource, int32_t layout,
        uint32_t pipelineStageFlags, uint32_t accessFlags, int32_t accessMode, UnityVulkanImage* outImage);
};

static const unsigned long long VulkanGuidHigh = 0x95355348d4ef4e11ULL;
static const unsigned long long VulkanGuidLow = 0x9789313dfcffcc87ULL;

// The two plugin events SkiaUnityRenderThread issues per draw, matching its constants.
static const int PrepareEvent = 0x534B01;
static const int DrawEvent = 0x534B02;

static IUnityGraphicsVulkan* s_vulkan;

extern "C" __declspec(dllexport) void __stdcall UnityPluginLoad(IUnityInterfaces* interfaces)
{
    s_vulkan = (IUnityGraphicsVulkan*)interfaces->GetInterfaceSplit(VulkanGuidHigh, VulkanGuidLow);
    if (!s_vulkan)
        return;
    // Prepare runs outside a render pass and with no queue access (Unity forbids resource access
    // otherwise), so its barrier is recorded into Unity's command buffer. Draw submits Skia's work
    // on Unity's queue after Unity has submitted everything recorded so far, barrier included.
    UnityVulkanPluginEventConfig prepare = { 2, 0, 1 };
    s_vulkan->ConfigureEvent(PrepareEvent, &prepare);
    UnityVulkanPluginEventConfig draw = { 2, 1, 1 | 2 };
    s_vulkan->ConfigureEvent(DrawEvent, &draw);
}

extern "C" __declspec(dllexport) void __stdcall UnityPluginUnload()
{
    s_vulkan = nullptr;
}

// True when Unity is running on Vulkan and handed this plugin its interface.
extern "C" __declspec(dllexport) bool SkiaUnityVulkan_IsAvailable()
{
    return s_vulkan != nullptr;
}

// Fills the handles Skia's Vulkan backend needs. False when Unity isn't running on Vulkan.
extern "C" __declspec(dllexport) bool SkiaUnityVulkan_GetInstance(
    void** instance, void** physicalDevice, void** device, void** queue, unsigned int* queueFamilyIndex)
{
    if (!s_vulkan)
        return false;
    UnityVulkanInstance v = s_vulkan->Instance();
    *instance = v.instance;
    *physicalDevice = v.physicalDevice;
    *device = v.device;
    *queue = v.graphicsQueue;
    *queueFamilyIndex = v.queueFamilyIndex;
    return v.device != nullptr && v.graphicsQueue != nullptr;
}

// The device's Vulkan version, which is what Skia may assume of Unity's device-level entry points.
extern "C" __declspec(dllexport) unsigned int SkiaUnityVulkan_ApiVersion()
{
    if (!s_vulkan)
        return 0;
    UnityVulkanInstance v = s_vulkan->Instance();
    typedef void* (__stdcall* GetInstanceProcAddr)(void* instance, const char* name);
    typedef void (__stdcall* GetPhysicalDeviceProperties)(void* physicalDevice, void* properties);
    GetInstanceProcAddr getProc = (GetInstanceProcAddr)v.getInstanceProcAddr;
    GetPhysicalDeviceProperties getProperties = (GetPhysicalDeviceProperties)getProc(v.instance, "vkGetPhysicalDeviceProperties");
    if (!getProperties)
        return 0;
    // VkPhysicalDeviceProperties is 824 bytes and apiVersion is its first field.
    unsigned int properties[256] = {};
    getProperties(v.physicalDevice, properties);
    return properties[0];
}

struct SkiaUnityVulkanImage
{
    uint64_t image;
    int32_t format;
    uint32_t usage;
    int32_t tiling;
    int32_t layout;
    int32_t samples;
    int32_t mipCount;
};

// Plugin event "prepare" only. Has Unity record a barrier that moves the texture to
// COLOR_ATTACHMENT_OPTIMAL for color attachment reads and writes, and tracks it as left there. The
// barrier runs before the "draw" event's Skia commands because Unity submits what it has recorded
// before that event. Returns false on failure.
extern "C" __declspec(dllexport) bool SkiaUnityVulkan_PrepareForDrawing(void* nativeTexture, SkiaUnityVulkanImage* out)
{
    if (!s_vulkan || !nativeTexture)
        return false;
    UnityVulkanImage image = {};
    // COLOR_ATTACHMENT_OPTIMAL (2), stage COLOR_ATTACHMENT_OUTPUT (0x400), access COLOR_ATTACHMENT_READ|WRITE (0x180), PipelineBarrier (1).
    if (!s_vulkan->AccessTexture(nativeTexture, nullptr, 2, 0x400, 0x180, 1, &image))
        return false;
    out->image = image.image;
    out->format = image.format;
    out->usage = image.usage;
    out->tiling = image.tiling;
    out->layout = image.layout;
    out->samples = image.samples;
    out->mipCount = image.mipCount;
    return true;
}
