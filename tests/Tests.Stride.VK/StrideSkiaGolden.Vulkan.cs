using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Tests.Shared;

/// <summary>
/// Vulkan half of <see cref="StrideSkiaGolden"/>. Unlike D3D11, Stride's Vulkan backend records
/// into command lists the caller creates and only runs them once they are closed and handed to
/// <c>GraphicsDevice.ExecuteCommandList</c> - so each method here owns a command list for the work
/// it records, and submits it before returning.
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

    private static partial byte[] ReadRgba(GraphicsDevice graphicsDevice, Texture texture)
    {
        using var commandList = CommandList.New(graphicsDevice);
        var pixels = texture.GetData<Color>(commandList);
        graphicsDevice.ExecuteCommandList(commandList.Close());
        return PackRgba(pixels);
    }
}
