using Stride.Graphics;

namespace Tests.Shared;

/// <summary>
/// D3D12 half of <see cref="StrideSkiaGolden"/>. Like Vulkan and unlike D3D11, Stride's D3D12
/// backend records into command lists the caller creates and runs them only once they are closed
/// and handed to <c>GraphicsDevice.ExecuteCommandList</c>, so each method owns and submits one.
/// </summary>
static partial class StrideSkiaGolden
{
    private static partial void Composite(GraphicsDevice graphicsDevice, Texture target, Action<GraphicsContext> draw)
    {
        using var commandList = CommandList.New(graphicsDevice);
        commandList.SetRenderTargetAndViewport(null, target);
        commandList.Clear(target, ClearColor);
        draw(new GraphicsContext(graphicsDevice, commandList: commandList));
        graphicsDevice.ExecuteCommandList(commandList.Close());
    }

    private static partial IDisposable BeginFrames(GraphicsDevice graphicsDevice, out Action<Texture, Action<GraphicsContext>> runFrame)
    {
        var commandList = CommandList.New(graphicsDevice);
        var graphicsContext = new GraphicsContext(graphicsDevice, commandList: commandList);
        bool open = true;
        runFrame = (target, draw) =>
        {
            if (!open)
                commandList.Reset();
            commandList.SetRenderTargetAndViewport(null, target);
            commandList.Clear(target, ClearColor);
            draw(graphicsContext);
            graphicsDevice.ExecuteCommandList(commandList.Close());
            open = false;
        };
        return commandList;
    }

    /// <summary>
    /// Draws the scene with <c>SkiaStrideD3D12DirectCanvas</c> straight into a bound Stride render target
    /// cleared as the other paths clear theirs, so it matches the same golden.
    /// </summary>
    internal static byte[] RenderSceneDirect(GraphicsDevice graphicsDevice)
    {
        using var target = Texture.New2D(
            graphicsDevice, GoldenScene.Width, GoldenScene.Height, PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        using var direct = new SkiaGameRendering.Stride.D3D12.SkiaStrideD3D12DirectCanvas(graphicsDevice);

        Composite(graphicsDevice, target, graphicsContext =>
        {
            direct.Begin(graphicsContext);
            GoldenScene.Draw(direct.Canvas);
            direct.End();
        });

        return ReadRgba(graphicsDevice, target);
    }

    /// <summary>
    /// <see cref="AssertNoPerFrameGrowth"/> for <c>SkiaStrideD3D12DirectCanvas</c>: same frame shape, with the
    /// scene drawn straight into the bound target. Each pass submits the command list and records into a fresh
    /// one, so this is what would show a per-pass command list, allocator or resource that is never released.
    /// </summary>
    internal static void AssertNoPerFrameGrowthDirect(GraphicsDevice graphicsDevice, Xunit.Abstractions.ITestOutputHelper output)
    {
        using var target = Texture.New2D(
            graphicsDevice, GoldenScene.Width, GoldenScene.Height, PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        using var direct = new SkiaGameRendering.Stride.D3D12.SkiaStrideD3D12DirectCanvas(graphicsDevice);
        using var frames = BeginFrames(graphicsDevice, out var runFrame);

        int frame = 0;
        FrameLeakCheck.AssertNoPerFrameGrowth(() =>
        {
            graphicsDevice.Begin();
            runFrame(target, graphicsContext =>
            {
                direct.Begin(graphicsContext);
                GoldenScene.Draw(direct.Canvas);
                direct.End();
            });
            graphicsDevice.End();
            if (++frame % 2 == 0)
                WaitForGpu(graphicsDevice);
        }, output);
    }

    private static partial byte[] ReadRgba(GraphicsDevice graphicsDevice, Texture texture)
    {
        using var commandList = CommandList.New(graphicsDevice);
        var pixels = texture.GetData<Stride.Core.Mathematics.Color>(commandList);
        graphicsDevice.ExecuteCommandList(commandList.Close());
        return PackRgba(pixels);
    }
}
