using System.Runtime.InteropServices;

/// <summary>
/// --async-via-layer-settings: turns MoltenVK's synchronous queue submits off through <c>VK_EXT_layer_settings</c> on
/// the instance instead of <c>MVK_CONFIG_SYNCHRONOUS_QUEUE_SUBMITS=0</c>. The library cannot see this route, so the
/// sample also passes <c>asynchronousQueueSubmits: true</c> to <c>SkiaVulkanContext</c>.
/// </summary>
static unsafe class MoltenVkLayerSettings
{
    public const string ExtensionName = "VK_EXT_layer_settings";
    const uint StructureTypeLayerSettingsCreateInfo = 1000496000;
    const uint LayerSettingTypeBool32 = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct LayerSettingsCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint settingCount;
        public LayerSetting* pSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LayerSetting
    {
        public IntPtr pLayerName;
        public IntPtr pSettingName;
        public uint type;
        public uint valueCount;
        public void* pValues;
    }

    static readonly uint* False = (uint*)NativeMemory.AllocZeroed(sizeof(uint));

    /// <summary>A leaked (process-lifetime) create info chained onto <paramref name="next"/>.</summary>
    public static LayerSettingsCreateInfo* SynchronousQueueSubmitsOff(void* next)
    {
        // MoltenVK matches the layer name "MoltenVK" and the setting name as the env var spells it (MVKInstance.mm).
        string[] names = ["MVK_CONFIG_SYNCHRONOUS_QUEUE_SUBMITS"];
        var settings = (LayerSetting*)NativeMemory.AllocZeroed((nuint)(names.Length * sizeof(LayerSetting)));
        for (var i = 0; i < names.Length; i++)
            settings[i] = new LayerSetting
            {
                pLayerName = Marshal.StringToHGlobalAnsi("MoltenVK"),
                pSettingName = Marshal.StringToHGlobalAnsi(names[i]),
                type = LayerSettingTypeBool32,
                valueCount = 1,
                pValues = False,
            };
        var info = (LayerSettingsCreateInfo*)NativeMemory.AllocZeroed((nuint)sizeof(LayerSettingsCreateInfo));
        *info = new LayerSettingsCreateInfo { sType = StructureTypeLayerSettingsCreateInfo, pNext = (IntPtr)next, settingCount = (uint)names.Length, pSettings = settings };
        return info;
    }
}
