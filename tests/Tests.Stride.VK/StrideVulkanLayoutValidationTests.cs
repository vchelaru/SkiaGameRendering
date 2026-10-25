using System.Runtime.InteropServices;
using System.Text;
using SkiaGameRendering;
using SkiaSharp;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.StrideVK;

/// <summary>
/// Skips unless the Vulkan loader can see the Khronos validation layer (<c>VK_LAYER_KHRONOS_validation</c>,
/// from the Vulkan SDK). Stride silently creates its debug instance without the layer when it is
/// missing, so running anyway would pass without validating anything. CI sets
/// <c>SKIAGAMERENDERING_REQUIRE_VK_VALIDATION=1</c>, which runs it anyway so a missing layer fails
/// (on its <c>layerEnabled</c> check) instead of skipping unnoticed.
/// </summary>
public sealed class VulkanValidationLayerFactAttribute : FactAttribute
{
    const string LayerName = "VK_LAYER_KHRONOS_validation";

    public VulkanValidationLayerFactAttribute()
    {
        if (!IsLayerAvailable() && Environment.GetEnvironmentVariable("SKIAGAMERENDERING_REQUIRE_VK_VALIDATION") != "1")
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
/// Synchronization validation is on too (see <c>VK_LAYER_ENABLES</c> below): it reports the
/// READ_AFTER_WRITE hazard that Stride before 4.4.1 had when its render target barrier carried no
/// <c>COLOR_ATTACHMENT_READ</c> access for a pass that loads the target (issue #122).
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

        // The layer reads this when the instance is created, which Stride does inside GraphicsDevice.New.
        const string EnablesVariable = "VK_LAYER_ENABLES";
        var previousEnables = Environment.GetEnvironmentVariable(EnablesVariable);
        Environment.SetEnvironmentVariable(EnablesVariable, "VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT");

        GlobalLogger.GlobalMessageLogged += OnMessage;
        try
        {
            using var device = GraphicsDevice.New(GraphicsAdapterFactory.DefaultAdapter, DeviceCreationFlags.Debug);
            device.ColorSpace = ColorSpace.Gamma;

            using var compositeTarget = Texture.New2D(
                device, 8, 8, PixelFormat.R8G8B8A8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
            using var canvas = new SkiaRenderTarget2D(device, 8, 8);

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

                // Then the same cycle through SkiaDirectCanvas, which hands the bound target to Skia and back.
                // In this test because the layer-enabled message is logged once per process.
                using var direct = new SkiaDirectCanvas(device);
                foreach (var color in new[] { SKColors.Red, SKColors.Blue, SKColors.Green })
                {
                    using var commandList = CommandList.New(device);
                    commandList.SetRenderTargetAndViewport(null, compositeTarget);
                    commandList.Clear(compositeTarget, new Color4(0f, 0f, 0f, 0f));
                    direct.Begin(new GraphicsContext(device, commandList: commandList));
                    direct.Canvas.Clear(color);
                    direct.End();
                    device.ExecuteCommandList(commandList.Close());
                }

                using var directReadback = CommandList.New(device);
                var directPixel = compositeTarget.GetData<Color>(directReadback)[0];
                device.ExecuteCommandList(directReadback.Close());

                Assert.Equal(new Color(0, 128, 0, 255), directPixel);

                // Then the swap chain back buffer in Stride's default Linear color space: an sRGB-format image that
                // Skia sees as its UNORM twin. Stride creates it without VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT, so the
                // layer reports here if viewing it as UNORM is invalid (issue #245).
                device.ColorSpace = ColorSpace.Linear;
                using var window = new HiddenWindow(8, 8);
                var parameters = new PresentationParameters(8, 8,
                    new WindowHandle(AppContextType.DesktopWinForms, null!, window.Handle), PixelFormat.R8G8B8A8_UNorm_SRgb);
                using var presenter = new SwapChainGraphicsPresenter(device, parameters);
                device.Presenter = presenter;
                try
                {
                    Assert.EndsWith("_SRgb", presenter.BackBuffer.ViewFormat.ToString());
                    foreach (var color in new[] { SKColors.Red, SKColors.Blue, SKColors.Green })
                    {
                        using var commandList = CommandList.New(device);
                        commandList.SetRenderTargetAndViewport(null, presenter.BackBuffer);
                        commandList.Clear(presenter.BackBuffer, new Color4(0f, 0f, 0f, 0f));
                        direct.Begin(new GraphicsContext(device, commandList: commandList));
                        direct.Canvas.Clear(color);
                        direct.End();
                        device.ExecuteCommandList(commandList.Close());
                        presenter.Present();
                    }
                }
                finally
                {
                    device.Presenter = null!;
                }
            }
            finally
            {
                // The context is static and pinned to this device; see StrideVulkanCompositeColorTests.
                canvas.Dispose();
                SkiaRenderer.Dispose();
            }
        }
        finally
        {
            GlobalLogger.GlobalMessageLogged -= OnMessage;
            Environment.SetEnvironmentVariable(EnablesVariable, previousEnables);
        }

        Assert.True(layerEnabled, "Stride did not report enabling the validation layer, so nothing was validated.");
        Assert.True(errors.Count == 0, "Vulkan validation messages:\n" + string.Join("\n", errors));
    }
}
