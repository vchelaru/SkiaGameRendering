using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Tests.Shared;

/// <summary>
/// D3D11 half of <see cref="StrideSkiaGolden"/>. Direct3D 11 supports exactly one
/// <see cref="CommandList"/> per device - it wraps the immediate context, not a deferred one, so
/// <c>CommandList.New</c> throws "Creation of additional Command Lists is not supported for
/// Direct3D 11". Both methods here therefore let <see cref="GraphicsContext"/> resolve the device's
/// own main command list (what a real <c>RenderDrawContext.GraphicsContext</c> also hands back)
/// instead of creating one, and there is nothing to submit afterwards.
/// </summary>
static partial class StrideSkiaGolden
{
    private static partial void Composite(GraphicsDevice graphicsDevice, Texture target, Action<GraphicsContext> draw)
    {
        var graphicsContext = new GraphicsContext(graphicsDevice);
        graphicsContext.CommandList.SetRenderTargetAndViewport(null, target);
        graphicsContext.CommandList.Clear(target, ClearColor);
        draw(graphicsContext);
    }

    private static partial byte[] ReadRgba(GraphicsDevice graphicsDevice, Texture texture) =>
        PackRgba(texture.GetData<Color>(new GraphicsContext(graphicsDevice).CommandList));
}
