using Xunit;
using static Tests.CoreVK.VulkanTestNative;

namespace Tests.CoreVK;

/// <summary>
/// Creates a real, minimal headless <c>VkInstance</c>/<c>VkPhysicalDevice</c>/<c>VkDevice</c>/
/// <c>VkQueue</c> against the system Vulkan loader - no validation layers, no extensions, no
/// swapchain/surface - so <c>VkSkiaSurfaceFactory</c> interop tests run against a real Vulkan
/// implementation without a window. This plays the same role for Vulkan that <c>WarpDevice</c> plays
/// for D3D11 (Core.ANGLE) and <c>WglContext</c> plays for GL (Core.OGL) - see the
/// <c>headless-gpu-testing</c> skill.
/// <para>
/// Unlike WARP (Microsoft's own software D3D11 rasterizer, bundled with every Windows install),
/// Vulkan has no first-party software implementation to force - whichever ICD the system loader
/// enumerates first is what this uses. On a dev box with a real GPU that's the real driver; in CI
/// (no GPU) that's Mesa lavapipe (llvmpipe's Vulkan ICD), selected via the <c>VK_ICD_FILENAMES</c>
/// environment variable set by <c>master.yml</c> - see the headless-gpu-testing skill's Vulkan
/// section for exactly how that's wired per platform.
/// </para>
/// </summary>
internal sealed unsafe class VulkanTestDevice : IDisposable
{
    internal IntPtr Instance { get; private set; }
    internal IntPtr PhysicalDevice { get; private set; }
    internal IntPtr Device { get; private set; }
    internal IntPtr Queue { get; private set; }
    internal uint GraphicsQueueFamilyIndex { get; private set; }
    internal uint ApiVersion { get; private set; } = VK_API_VERSION_1_1;

    internal VulkanTestDevice()
    {
        CreateInstance();
        SelectPhysicalDevice();
        FindGraphicsQueueFamily();
        CreateDevice();
    }

    void CreateInstance()
    {
        var appInfo = new VkApplicationInfo
        {
            sType = VK_STRUCTURE_TYPE_APPLICATION_INFO,
            apiVersion = ApiVersion,
        };
        var createInfo = new VkInstanceCreateInfo
        {
            sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,
            pApplicationInfo = (IntPtr)(&appInfo),
        };
        int hr = vkCreateInstance(&createInfo, IntPtr.Zero, out var instance);
        Assert.True(hr == VK_SUCCESS, $"vkCreateInstance failed. VkResult: {hr}");
        Instance = instance;
    }

    void SelectPhysicalDevice()
    {
        uint count = 0;
        vkEnumeratePhysicalDevices(Instance, ref count, null);
        Assert.True(count > 0, "vkEnumeratePhysicalDevices reported zero physical devices - no Vulkan ICD is available (see the headless-gpu-testing skill's Vulkan section for CI setup).");

        var devices = new IntPtr[count];
        int hr = vkEnumeratePhysicalDevices(Instance, ref count, devices);
        Assert.True(hr == VK_SUCCESS, $"vkEnumeratePhysicalDevices failed. VkResult: {hr}");
        Assert.True(devices[0] != IntPtr.Zero, "vkEnumeratePhysicalDevices returned a null handle for devices[0].");
        PhysicalDevice = devices[0];
    }

    void FindGraphicsQueueFamily()
    {
        uint count = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref count, null);
        Assert.True(count > 0, "vkGetPhysicalDeviceQueueFamilyProperties reported zero queue families.");

        var properties = new VkQueueFamilyProperties[count];
        vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, ref count, properties);

        for (uint i = 0; i < count; i++)
        {
            if ((properties[i].queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0)
            {
                GraphicsQueueFamilyIndex = i;
                return;
            }
        }
        Assert.Fail("No queue family with VK_QUEUE_GRAPHICS_BIT was found.");
    }

    void CreateDevice()
    {
        float priority = 1.0f;
        var queueCreateInfo = new VkDeviceQueueCreateInfo
        {
            sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,
            queueFamilyIndex = GraphicsQueueFamilyIndex,
            queueCount = 1,
            pQueuePriorities = (IntPtr)(&priority),
        };
        var deviceCreateInfo = new VkDeviceCreateInfo
        {
            sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,
            queueCreateInfoCount = 1,
            pQueueCreateInfos = (IntPtr)(&queueCreateInfo),
            // No extensions, no explicit feature struct (pEnabledFeatures = null - Vulkan treats
            // that as "enable no optional features"), matching the minimal-surface goal documented
            // on VulkanTestNative.
        };
        int hr = vkCreateDevice(PhysicalDevice, &deviceCreateInfo, IntPtr.Zero, out var device);
        Assert.True(hr == VK_SUCCESS, $"vkCreateDevice failed. VkResult: {hr}");
        Device = device;

        vkGetDeviceQueue(Device, GraphicsQueueFamilyIndex, 0, out var queue);
        Queue = queue;
    }

    /// <summary>
    /// Finds a memory type index satisfying both <paramref name="memoryTypeBits"/> (from
    /// <c>vkGetImageMemoryRequirements</c>/<c>vkGetBufferMemoryRequirements</c>) and
    /// <paramref name="requiredProperties"/>. Vulkan has no query for "the best" memory type - this
    /// is the standard linear-scan pattern every Vulkan app implements itself.
    /// </summary>
    internal uint FindMemoryTypeIndex(uint memoryTypeBits, uint requiredProperties)
    {
        vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out var memProperties);
        for (int i = 0; i < memProperties.memoryTypeCount; i++)
        {
            bool typeAllowed = (memoryTypeBits & (1u << i)) != 0;
            bool propertiesMatch = (memProperties.memoryTypes[i].propertyFlags & requiredProperties) == requiredProperties;
            if (typeAllowed && propertiesMatch)
                return (uint)i;
        }
        Assert.Fail($"No Vulkan memory type satisfies bits 0x{memoryTypeBits:X} with required properties 0x{requiredProperties:X}.");
        return 0;
    }

    public void Dispose()
    {
        if (Device != IntPtr.Zero)
        {
            vkDeviceWaitIdle(Device);
            vkDestroyDevice(Device, IntPtr.Zero);
        }
        if (Instance != IntPtr.Zero)
            vkDestroyInstance(Instance, IntPtr.Zero);
    }
}
