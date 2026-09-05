using System.Reflection;
using Stride.Graphics;
using Tests.Shared;
using Vortice.Vulkan;
using Xunit;

namespace Tests.StrideVK;

/// <summary>
/// Pins every Stride Vulkan internal <c>SkiaStrideVulkanContext</c> reaches by string
/// (src/SkiaGameRendering.Stride.VK/SkiaStrideVulkanContext.cs):
/// <c>GraphicsDevice.NativeInstance</c>/<c>NativePhysicalDevice</c>/<c>NativeDevice</c> (properties),
/// <c>GraphicsDevice.NativeCommandQueue</c>/<c>QueueLock</c> (fields), and
/// <c>Texture.NativeImage</c>/<c>NativeLayout</c>/<c>NativeFormat</c> (fields).
///
/// Everything else the adapter needs - <c>Texture.New2D</c>, <c>TextureFlags</c>,
/// <c>GraphicsDevice</c>/<c>Texture</c> themselves - is public and used as a normal compile-time
/// member, so a rename there already fails the build; these are the only places a Stride version
/// bump can break the adapter silently at runtime instead, since they only resolve by name against a
/// live <c>GraphicsDevice</c>/<c>Texture</c> reflectively.
///
/// <c>Tests.Stride.VK.csproj</c> sets <c>StrideGraphicsApi=Vulkan</c> so
/// <c>typeof(GraphicsDevice)</c>/<c>typeof(Texture)</c> here bind to the Vulkan-built
/// <c>Stride.Graphics.dll</c> variant (see that csproj's comment) - without it, these types would
/// bind to the Windows-default Direct3D11 variant, which has none of these members at all.
/// </summary>
public sealed class StrideVulkanReflectionTests
{
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void NativeInstanceProperty_ResolvesToVkInstance()
    {
        EngineReflectionPin.RequirePropertyOfType(
            typeof(GraphicsDevice), "NativeInstance", NonPublicInstance, typeof(VkInstance));
    }

    [Fact]
    public void NativePhysicalDeviceProperty_ResolvesToVkPhysicalDevice()
    {
        EngineReflectionPin.RequirePropertyOfType(
            typeof(GraphicsDevice), "NativePhysicalDevice", NonPublicInstance, typeof(VkPhysicalDevice));
    }

    [Fact]
    public void NativeDeviceProperty_ResolvesToVkDevice()
    {
        EngineReflectionPin.RequirePropertyOfType(
            typeof(GraphicsDevice), "NativeDevice", NonPublicInstance, typeof(VkDevice));
    }

    [Fact]
    public void NativeCommandQueueField_ResolvesToVkQueue()
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsDevice), "NativeCommandQueue", NonPublicInstance);

        Assert.Equal(typeof(VkQueue), field.FieldType);
    }

    [Fact]
    public void QueueLockField_ResolvesToObject()
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsDevice), "QueueLock", NonPublicInstance);

        Assert.Equal(typeof(object), field.FieldType);
    }

    [Fact]
    public void NativeImageField_ResolvesToVkImage()
    {
        var field = EngineReflectionPin.RequireField(typeof(Texture), "NativeImage", NonPublicInstance);

        Assert.Equal(typeof(VkImage), field.FieldType);
    }

    [Fact]
    public void NativeLayoutField_ResolvesToVkImageLayout()
    {
        var field = EngineReflectionPin.RequireField(typeof(Texture), "NativeLayout", NonPublicInstance);

        Assert.Equal(typeof(VkImageLayout), field.FieldType);
    }

    [Fact]
    public void NativeFormatField_ResolvesToVkFormat()
    {
        var field = EngineReflectionPin.RequireField(typeof(Texture), "NativeFormat", NonPublicInstance);

        Assert.Equal(typeof(VkFormat), field.FieldType);
    }
}
