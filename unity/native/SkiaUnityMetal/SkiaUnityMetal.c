// Hands Unity's Metal device and command queue to SkiaGameRendering.Unity, which gives both to
// Core.Metal, and lets it commit Unity's current command buffer before Skia submits its own. Unity's
// C# API exposes none of this. Built by eng/build-unity-metal-plugin.sh; the output is committed.
//
// The interface structs are declared here, not included from Unity's PluginAPI headers
// (IUnityInterface.h, IUnityGraphicsMetal.h), so the build doesn't need a Unity install. Only the
// members used are typed; recheck the layout against those headers after a Unity upgrade.

typedef struct
{
    void* GetInterface;
    void* RegisterInterface;
    void* (*GetInterfaceSplit)(unsigned long long guidHigh, unsigned long long guidLow);
    void* RegisterInterfaceSplit;
} IUnityInterfaces;

// IUnityGraphicsMetalV2's leading members, in header order.
typedef struct
{
    void* (*CommitCurrentCommandBuffer)(void);
    void* (*CommandQueue)(void);
    void* (*MetalBundle)(void);
    void* (*MetalDevice)(void);
} IUnityGraphicsMetalV2;

static const unsigned long long MetalV2GuidHigh = 0xF58857784FEF46ECULL;
static const unsigned long long MetalV2GuidLow = 0x9DB7A8803B87DA3DULL;

static IUnityGraphicsMetalV2* s_metal;

__attribute__((visibility("default"))) void UnityPluginLoad(IUnityInterfaces* interfaces)
{
    s_metal = (IUnityGraphicsMetalV2*)interfaces->GetInterfaceSplit(MetalV2GuidHigh, MetalV2GuidLow);
}

__attribute__((visibility("default"))) void UnityPluginUnload(void)
{
    s_metal = 0;
}

// Null when Unity isn't running on Metal or never called UnityPluginLoad.
__attribute__((visibility("default"))) void* SkiaUnityMetal_Device(void)
{
    return s_metal ? s_metal->MetalDevice() : 0;
}

__attribute__((visibility("default"))) void* SkiaUnityMetal_CommandQueue(void)
{
    return s_metal ? s_metal->CommandQueue() : 0;
}

// Render thread only. Commits what Unity has encoded so far, so a command buffer committed next
// runs after it; Unity starts a new one for whatever it encodes later.
__attribute__((visibility("default"))) void SkiaUnityMetal_CommitCurrentCommandBuffer(void)
{
    if (s_metal)
        s_metal->CommitCurrentCommandBuffer();
}
