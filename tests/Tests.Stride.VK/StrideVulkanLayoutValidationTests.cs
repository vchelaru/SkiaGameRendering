using System.Runtime.InteropServices;
using System.Text;
using SkiaGameRendering.Stride.VK;
using SkiaSharp;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Xunit;

namespace Tests.StrideVK;

/// <summary>
/// Skips unless the Vulkan loader can see the Khronos validation layer (<c>VK_LAYER_KHRONOS_validation</c>,
/// from the Vulkan SDK). Stride silently creates its debug instance without the layer when it is
/// missing, so running anyway would pass without validating anything.
/// </summary>
public sealed class VulkanValidationLayerFactAttribute : FactAttribute
{
    const string LayerName = "VK_LAYER_KHRONOS_validation";

    public VulkanValidationLayerFactAttribute()
    {
        if (!IsLayerAvailable())
            Skip = $"{LayerName} is not installed (Vulkan SDK), or no Vulkan loader is present.";
    }

    static unsafe bool IsLayerAvailable()
    {
        try
        {
            uint count = 0;
            if (vkEnumerateInstanceLayerProperties(ref count, null) != 0 || count == 0)
                return false;

            // VkLayerProperties: char layerName[256], uint32 specVersion, uint32 implementationVersion,
            // char description[256].
            const int size = 256 + 4 + 4 + 256;
            var buffer = new byte[size * count];
            fixed (byte* p = buffer)
            {
                if (vkEnumerateInstanceLayerProperties(ref count, p) < 0)
                    return false;
            }
            for (var i = 0; i < count; i++)
            {
                var name = buffer.AsSpan(i * size, 256);
                var end = name.IndexOf((byte)0);
                if (Encoding.ASCII.GetString(end < 0 ? name : name[..end]) == LayerName)
                    return true;
            }
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    [DllImport("vulkan-1")]
    static extern unsafe int vkEnumerateInstanceLayerProperties(ref uint pPropertyCount, byte* pProperties);
}

/// <summary>
/// Runs the Skia draw / Stride composite cycle for several frames with the Khronos validation layer
/// on, and fails on any error it reports. Stride's Vulkan <c>SpriteBatch</c> samples through a
/// descriptor that declares <c>SHADER_READ_ONLY_OPTIMAL</c> without moving the image there, and Skia
/// keeps believing the image is in the layout it last left it in; lavapipe renders the right pixels
/// either way, so only the layer catches a wrong handoff. Stride routes the layer's messages to its
/// logger from the debug messenger it installs for <c>DeviceCreationFlags.Debug</c>. The Vulkan
/// analog of <c>Tests.Stride.D3D12</c>'s <c>StrideD3D12BarrierValidationTests</c>.
/// </summary>
public sealed class StrideVulkanLayoutValidationTests
{
    [VulkanValidationLayerFact]
    public void DrawCompositeCycle_ReportsNoValidationErrors()
    {
        var errors = new List<string>();
        var layerEnabled = false;
        void OnMessage(ILogMessage message)
        {
            if (message.Text.Contains("Vulkan validation layer enabled"))
                layerEnabled = true;
            if (message.Type >= LogMessageType.Warning && message.Text.StartsWith("[Vulkan]"))
                lock (errors) errors.Add(message.Text);
        }

        GlobalLogger.GlobalMessageLogged += OnMessage;
        try
        {
            using var device = GraphicsDevice.New(GraphicsAdapterFactory.DefaultAdapter, DeviceCreationFlags.Debug);
            device.ColorSpace = ColorSpace.Gamma;

            using var compositeTarget = Texture.New2D(
                device, 8, 8, PixelFormat.R8G8B8A8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
            using var canvas = new SkiaStrideVulkanRenderTarget2D(device, 8, 8);

            try
            {
                foreach (var color in new[] { SKColors.Red, SKColors.Blue, SKColors.Green })
                {
                    using var commandList = CommandList.New(device);
                    commandList.SetRenderTargetAndViewport(null, compositeTarget);
                    commandList.Clear(compositeTarget, new Color4(0f, 0f, 0f, 0f));
                    canvas.Begin();
                    canvas.Canvas.Clear(color);
                    canvas.End(new GraphicsContext(device, commandList: commandList));
                    device.ExecuteCommandList(commandList.Close());
                }

                using var readback = CommandList.New(device);
                var pixel = compositeTarget.GetData<Color>(readback)[0];
                device.ExecuteCommandList(readback.Close());

                Assert.Equal(new Color(0, 128, 0, 255), pixel);
            }
            finally
            {
                // The context is static and pinned to this device; see StrideVulkanCompositeColorTests.
                canvas.Dispose();
                SkiaStrideVulkanRenderer.Dispose();
            }
        }
        finally
        {
            GlobalLogger.GlobalMessageLogged -= OnMessage;
        }

        Assert.True(layerEnabled, "Stride did not report enabling the validation layer, so nothing was validated.");
        Assert.True(errors.Count == 0, "Vulkan validation messages:\n" + string.Join("\n", errors));
    }
}
