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

    private static partial byte[] ReadRgba(GraphicsDevice graphicsDevice, Texture texture)
    {
        using var commandList = CommandList.New(graphicsDevice);
        var pixels = texture.GetData<Stride.Core.Mathematics.Color>(commandList);
        graphicsDevice.ExecuteCommandList(commandList.Close());
        return PackRgba(pixels);
    }
}
