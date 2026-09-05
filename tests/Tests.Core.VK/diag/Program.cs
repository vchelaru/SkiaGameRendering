using System.Runtime.InteropServices;

unsafe
{
    Console.WriteLine("VK_ICD_FILENAMES=" + Environment.GetEnvironmentVariable("VK_ICD_FILENAMES"));
    Console.WriteLine("VK_LOADER_DEBUG=" + Environment.GetEnvironmentVariable("VK_LOADER_DEBUG"));

    var appInfo = new VkApplicationInfo
    {
        sType = 0, // VK_STRUCTURE_TYPE_APPLICATION_INFO
        apiVersion = (1 << 22) | (1 << 12), // VK_API_VERSION_1_1
    };
    var createInfo = new VkInstanceCreateInfo
    {
        sType = 1, // VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO
        pApplicationInfo = (IntPtr)(&appInfo),
    };

    int hr = vkCreateInstance(&createInfo, IntPtr.Zero, out var instance);
    Console.WriteLine($"vkCreateInstance -> {hr}");
    if (hr == 0)
    {
        uint count = 0;
        vkEnumeratePhysicalDevices(instance, ref count, null);
        Console.WriteLine($"physical device count -> {count}");
    }
}

[StructLayout(LayoutKind.Sequential)]
struct VkApplicationInfo
{
    public int sType;
    public IntPtr pNext;
    public IntPtr pApplicationName;
    public uint applicationVersion;
    public IntPtr pEngineName;
    public uint engineVersion;
    public uint apiVersion;
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct VkInstanceCreateInfo
{
    public int sType;
    public IntPtr pNext;
    public uint flags;
    public IntPtr pApplicationInfo;
    public uint enabledLayerCount;
    public IntPtr ppEnabledLayerNames;
    public uint enabledExtensionCount;
    public IntPtr ppEnabledExtensionNames;
}

static partial class Program
{
    [DllImport("vulkan-1.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern unsafe int vkCreateInstance(VkInstanceCreateInfo* pCreateInfo, IntPtr pAllocator, out IntPtr pInstance);

    [DllImport("vulkan-1.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern int vkEnumeratePhysicalDevices(IntPtr instance, ref uint pCount, IntPtr[]? pPhysicalDevices);
}
