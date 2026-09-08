using Stride.Core.Mathematics;
using Stride.Graphics;

namespace Tests.Shared;

/// <summary>
/// Stride's counterpart to <see cref="EngineSkiaGolden"/>: draws <see cref="GoldenScene"/> through
/// the backend's own render-target type and reads the result back as tightly packed RGBA.
/// <para>
/// The two Stride backends expose the same members under different type names
/// (<c>SkiaStrideRenderTarget2D</c> against <c>SkiaStrideVulkanRenderTarget2D</c>), so each test
/// project aliases its own to <c>SkiaStrideCanvas</c> in its csproj and this one file serves both.
/// Everything else here is <c>Stride.Graphics</c>, whose type names are identical in the D3D11 and
/// Vulkan builds of the assembly. What genuinely differs - how a command list is obtained and
/// submitted - is the pair of partial methods each project implements.
/// </para>
/// <para>
/// Both entry points run on a <see cref="ColorSpace.Gamma"/> device, where the readback is the
/// scene's own colors and the two paths are pixel-identical. Under Stride's default
/// <see cref="ColorSpace.Linear"/> pipeline neither is: the backend hands Skia a linear color space
/// so Stride's own sRGB encode-on-write lands the right color, which leaves the Skia texture holding
/// linearized values (the scene's 220,60,40 rect reads back as 182,12,5) and the composite target
/// holding them re-encoded after 8-bit linear storage (220,61,38 for that rect, and up to 6 channel
/// steps out on the scene's dark background, where 8-bit linear quantizes hardest).
/// <para>
/// That pipeline is what <c>StrideCompositeColorTests</c> / <c>StrideVulkanCompositeColorTests</c>
/// cover, with a solid mid-tone color and a tolerance. These tests cover the half those can't -
/// shapes, antialiasing, gradients and blending - and want an exact comparison, so they take the
/// color space that gives them one.
/// </para>
/// </para>
/// </summary>
static partial class StrideSkiaGolden
{
    /// <summary>
    /// Draws the scene and reads back the Skia texture itself, with no Stride draw in between - so
    /// a failure here is in the Skia-to-Stride-texture path and nowhere else.
    /// </summary>
    internal static byte[] RenderSceneToSkiaTexture(GraphicsDevice graphicsDevice)
    {
        using var canvas = new SkiaStrideCanvas(graphicsDevice, GoldenScene.Width, GoldenScene.Height);
        canvas.Begin();
        GoldenScene.Draw(canvas.Canvas);
        canvas.EndWithoutDrawing();
        return ReadRgba(graphicsDevice, canvas.Texture);
    }

    /// <summary>
    /// The round trip a game actually performs: bind a Stride render target, draw the scene with
    /// Skia, and let the canvas's own <c>SpriteBatch</c> composite it back. Matching the same golden
    /// as <see cref="RenderSceneToSkiaTexture"/> means Stride sampled the shared texture correctly
    /// and its pipeline state survived the Skia draw - the backend swaps the D3D11 device-context
    /// state out to ANGLE's during the draw and back in before the composite, and a device left in
    /// Skia's state would not composite a pixel-identical copy.
    /// </summary>
    internal static byte[] RenderSceneCompositedByEngine(GraphicsDevice graphicsDevice)
    {
        // Stands in for the presented back buffer. Non-sRGB because this device is Gamma - see the
        // class doc comment, and StrideCompositeColorTests for the sRGB-format Linear case.
        using var compositeTarget = Texture.New2D(
            graphicsDevice, GoldenScene.Width, GoldenScene.Height, PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        using var canvas = new SkiaStrideCanvas(graphicsDevice, GoldenScene.Width, GoldenScene.Height);

        Composite(graphicsDevice, compositeTarget, graphicsContext =>
        {
            canvas.Begin();
            GoldenScene.Draw(canvas.Canvas);
            canvas.End(graphicsContext);
        });

        return ReadRgba(graphicsDevice, compositeTarget);
    }

    /// <summary>
    /// Binds <paramref name="target"/> as the render target, clears it, then runs
    /// <paramref name="draw"/> against a <see cref="GraphicsContext"/> wrapping the same command
    /// list, and submits whatever that recorded. Real <c>GraphicsCompositor</c>/
    /// <c>RenderDrawContext</c> machinery is <c>Game</c>-only; this is the same shape by hand, and
    /// the exact path <c>SkiaStrideSceneRenderer.DrawCore</c> drives every frame.
    /// <para>
    /// The bind happens before <paramref name="draw"/> deliberately: the canvas's <c>Begin</c> swaps
    /// the engine's own graphics state out and its <c>End</c> swaps it back in right before the
    /// composite, so the target has to be part of the state that gets restored. Binding it inside
    /// the Skia pass would set it against the swapped-out state, where the composite never sees it.
    /// </para>
    /// </summary>
    private static partial void Composite(GraphicsDevice graphicsDevice, Texture target, Action<GraphicsContext> draw);

    /// <summary>Reads a Stride texture back through its own <c>GetData</c>, as RGBA8888 bytes.</summary>
    private static partial byte[] ReadRgba(GraphicsDevice graphicsDevice, Texture texture);

    /// <summary>
    /// Shared tail of both <see cref="ReadRgba"/> implementations. <see cref="Color"/> is Stride's
    /// RGBA byte struct, so this is a straight unpack.
    /// </summary>
    static byte[] PackRgba(Color[] pixels)
    {
        var rgba = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            rgba[i * 4] = pixels[i].R;
            rgba[i * 4 + 1] = pixels[i].G;
            rgba[i * 4 + 2] = pixels[i].B;
            rgba[i * 4 + 3] = pixels[i].A;
        }

        return rgba;
    }

    /// <summary>Cleared to transparent black, so an unwritten pixel reads as obviously wrong.</summary>
    static readonly Color4 ClearColor = new(0f, 0f, 0f, 0f);
}
