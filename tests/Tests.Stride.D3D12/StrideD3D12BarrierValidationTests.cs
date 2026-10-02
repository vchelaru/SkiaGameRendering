using SkiaGameRendering.Stride.D3D12;
using SkiaSharp;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using Xunit;

namespace Tests.StrideD3D12;

/// <summary>
/// Skips unless the D3D12 debug layer (<c>d3d12SDKLayers.dll</c>, the Windows "Graphics Tools"
/// optional feature) is installed.
/// </summary>
public sealed class D3D12DebugLayerFactAttribute : FactAttribute
{
    public D3D12DebugLayerFactAttribute()
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "d3d12SDKLayers.dll")))
            Skip = "The D3D12 debug layer (Graphics Tools optional feature) is not installed.";
    }
}

/// <summary>
/// Runs the Skia draw / Stride composite cycle for several frames with the D3D12 debug layer and
/// GPU-based validation on, and fails on any error they report. WARP renders the right pixels even
/// when the texture sits in the wrong state, so the pixel tests alone cannot catch a broken handoff
/// between Stride's enhanced barriers and Skia's legacy ones; this is what does. Stride routes the
/// debug layer's messages to its logger each time it executes a command list.
/// </summary>
public sealed class StrideD3D12BarrierValidationTests
{
    /// <summary>
    /// Skia's own descriptor-heap setup asks every heap for its GPU start handle, shader-visible or
    /// not; seen once per heap when the Skia context is created, and never from Stride alone.
    /// </summary>
    const string SkiaHeapStartMessage = "GetGPUDescriptorHandleForHeapStart is invalid";

    [D3D12DebugLayerFact]
    public void DrawCompositeCycle_ReportsNoDebugLayerErrors()
    {
        EnableDebugLayerWithGpuValidation();

        var errors = new List<string>();
        void OnMessage(ILogMessage message)
        {
            if (message.Type >= LogMessageType.Error && !message.Text.Contains(SkiaHeapStartMessage))
                lock (errors) errors.Add(message.Text);
        }

        Environment.SetEnvironmentVariable("STRIDE_GRAPHICS_SOFTWARE_RENDERING", "1");
        using var device = GraphicsDevice.New(GraphicsAdapterFactory.DefaultAdapter, DeviceCreationFlags.Debug);
        device.DebugGpuValidationEnabled = true;
        device.ColorSpace = ColorSpace.Gamma;

        GlobalLogger.GlobalMessageLogged += OnMessage;
        try
        {
            using var compositeTarget = Texture.New2D(
                device, 8, 8, PixelFormat.R8G8B8A8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
            using var canvas = new SkiaStrideD3D12RenderTarget2D(device, 8, 8);

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

            // One more submission so Stride drains the messages the last frame produced.
            using var readback = CommandList.New(device);
            var pixel = compositeTarget.GetData<Color>(readback)[0];
            device.ExecuteCommandList(readback.Close());

            Assert.Equal(new Color(0, 128, 0, 255), pixel);
            Assert.Equal(GraphicsDeviceStatus.Normal, device.GraphicsDeviceStatus);
        }
        finally
        {
            SkiaStrideD3D12Renderer.Dispose();
            GlobalLogger.GlobalMessageLogged -= OnMessage;
        }

        Assert.True(errors.Count == 0, "D3D12 debug layer errors:\n" + string.Join("\n", errors));
    }

    /// <summary>
    /// Stride only turns on the basic debug layer for <c>DeviceCreationFlags.Debug</c>. GPU-based
    /// validation, which is what checks a texture's layout when a shader samples it, has to be
    /// switched on before the device exists, and it stays on for the rest of the process.
    /// </summary>
    static unsafe void EnableDebugLayerWithGpuValidation()
    {
        ComPtr<ID3D12Debug1> debug = default;
        var hr = D3D12.GetApi().GetDebugInterface(out debug);
        Assert.True(hr >= 0, $"D3D12GetDebugInterface failed: 0x{hr:X8}");
        try
        {
            debug.Handle->EnableDebugLayer();
            debug.Handle->SetEnableGPUBasedValidation(true);
        }
        finally
        {
            debug.Dispose();
        }
    }
}
