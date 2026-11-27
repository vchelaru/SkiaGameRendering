using System.Runtime.InteropServices;
using SkiaGameRendering.Core.VK;
using Xunit;

namespace Tests.CoreVK;

/// <summary>
/// <see cref="VkMetalObjectExport"/>, the MoltenVK path Skia uses on macOS. The export itself needs MoltenVK, so
/// these cover what runs anywhere: the struct layouts against the Vulkan headers, the refusal on a device without
/// <c>VK_EXT_metal_objects</c>.
/// </summary>
public sealed class VkMetalObjectExportTests
{
    [Fact]
    public void Structs_MatchTheVulkanHeaderLayout()
    {
        Assert.Equal(24, Marshal.SizeOf<VkMetalObjectExport.ExportMetalObjectCreateInfo>());
        Assert.Equal(16, (int)Marshal.OffsetOf<VkMetalObjectExport.ExportMetalObjectCreateInfo>("exportObjectType"));
        Assert.Equal(16, Marshal.SizeOf<VkMetalObjectExport.ExportMetalObjectsInfo>());
        Assert.Equal(16, (int)Marshal.OffsetOf<VkMetalObjectExport.ExportMetalDeviceInfo>("mtlDevice"));
        Assert.Equal(24, (int)Marshal.OffsetOf<VkMetalObjectExport.ExportMetalCommandQueueInfo>("mtlCommandQueue"));
        Assert.Equal(16, (int)Marshal.OffsetOf<VkMetalObjectExport.ExportMetalTextureInfo>("image"));
        Assert.Equal(40, (int)Marshal.OffsetOf<VkMetalObjectExport.ExportMetalTextureInfo>("plane"));
        Assert.Equal(48, (int)Marshal.OffsetOf<VkMetalObjectExport.ExportMetalTextureInfo>("mtlTexture"));
        Assert.Equal(56, Marshal.SizeOf<VkMetalObjectExport.ExportMetalTextureInfo>());
        Assert.Equal(16, (int)Marshal.OffsetOf<VkMetalObjectExport.ExportMetalSharedEventInfo>("semaphore"));
        Assert.Equal(32, (int)Marshal.OffsetOf<VkMetalObjectExport.ExportMetalSharedEventInfo>("mtlSharedEvent"));
        Assert.Equal(40, Marshal.SizeOf<VkMetalObjectExport.ExportMetalSharedEventInfo>());
    }

    [Fact]
    public void Export_OnADeviceWithoutTheExtension_NamesIt()
    {
        using var vk = new VulkanTestDevice();

        var error = Assert.Throws<InvalidOperationException>(() => VkMetalObjectExport.ExportDevice(vk.Device));
        Assert.Contains(VkMetalObjectExport.ExtensionName, error.Message);
    }
}
