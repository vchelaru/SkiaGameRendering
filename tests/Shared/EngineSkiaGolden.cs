using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;

namespace Tests.Shared;

/// <summary>
/// Drives <see cref="GoldenScene"/> through the engine-facing API - <see cref="SkiaRenderTarget2D"/>
/// on a live <see cref="GraphicsDevice"/> - and reads the result back as tightly packed RGBA, ready
/// for <see cref="GoldenImage"/>. Linked into each engine test project rather than each writing its
/// own: MonoGame and KNI expose the same type names here, so one file covers both.
/// <para>
/// This is the layer the <c>Tests.Core.*</c> golden tests can't reach. They drive a
/// <c>Core.*</c> surface factory directly against a texture they allocated themselves; these calls
/// go through the engine's own texture allocation, the backend's reflection into engine internals,
/// and whatever state the engine had set before the Skia draw.
/// </para>
/// </summary>
static class EngineSkiaGolden
{
    /// <summary>
    /// Draws the scene and reads back <see cref="SkiaRenderTarget2D.Texture"/> itself, with no
    /// engine draw in between - so a failure here is in the Skia-to-engine-texture path and nowhere
    /// else. <c>EndWithoutDrawing</c> rather than <c>End</c> for exactly that reason.
    /// </summary>
    internal static byte[] RenderSceneToSkiaTexture(GraphicsDevice graphicsDevice)
    {
        using var canvas = new SkiaRenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height);
        canvas.Begin();
        GoldenScene.Draw(canvas.Canvas);
        canvas.EndWithoutDrawing();
        return ReadRgba(canvas.Texture);
    }

    /// <summary>
    /// The whole round trip a game actually performs: bind an engine render target, draw the scene
    /// with Skia, and let <see cref="SkiaRenderTarget2D.End"/> composite it back through the
    /// engine's own <see cref="SpriteBatch"/>. Matching the same golden as
    /// <see cref="RenderSceneToSkiaTexture"/> means the engine sampled the shared texture correctly
    /// and its pipeline state survived the Skia draw - a device left in Skia's state would not
    /// composite a pixel-identical copy.
    /// </summary>
    internal static byte[] RenderSceneCompositedByEngine(GraphicsDevice graphicsDevice)
    {
        using var renderTarget = new RenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height,
            false, SurfaceFormat.Color, DepthFormat.None);
        using var canvas = new SkiaRenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height);

        graphicsDevice.SetRenderTarget(renderTarget);
        try
        {
            graphicsDevice.Clear(Color.Black);
            canvas.Begin();
            GoldenScene.Draw(canvas.Canvas);
            canvas.End();
        }
        finally
        {
            graphicsDevice.SetRenderTarget(null);
        }

        return ReadRgba(renderTarget);
    }

    /// <summary>Reads a texture back through the engine's own <c>GetData</c>, as RGBA8888 bytes.</summary>
    internal static byte[] ReadRgba(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);

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
}
