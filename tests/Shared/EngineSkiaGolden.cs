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

    /// <summary>
    /// The scene drawn with <see cref="SkiaDirectCanvas"/> into a bound engine render
    /// target, with no intermediate texture. Same golden as the other two.
    /// </summary>
    internal static byte[] RenderSceneDirectToRenderTarget(GraphicsDevice graphicsDevice)
    {
        using var renderTarget = new RenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height,
            false, SurfaceFormat.Color, DepthFormat.None);

        graphicsDevice.SetRenderTarget(renderTarget);
        try
        {
            graphicsDevice.Clear(Color.Black);
            DrawDirect(graphicsDevice);
        }
        finally
        {
            graphicsDevice.SetRenderTarget(null);
        }

        return ReadRgba(renderTarget);
    }

    /// <summary>The scene drawn with <see cref="SkiaDirectCanvas"/> straight onto the back buffer.</summary>
    internal static byte[] RenderSceneDirectToBackBuffer(GraphicsDevice graphicsDevice)
    {
        graphicsDevice.SetRenderTarget(null);
        graphicsDevice.Clear(Color.Black);
        DrawDirect(graphicsDevice);

        var pixels = new Color[GoldenScene.Width * GoldenScene.Height];
        graphicsDevice.GetBackBufferData(pixels);
        return ToRgba(pixels);
    }

    /// <summary>
    /// One <see cref="SkiaRenderTarget2D"/> drawn before and after a <see cref="SkiaDirectCanvas"/> pass on
    /// the back buffer, then read back. A backend that shares one GPU context between the two, and whose
    /// direct pass leaves that context's cached state out of step, fails the second draw.
    /// </summary>
    internal static byte[] RenderSceneToSkiaTextureAroundDirectDraw(GraphicsDevice graphicsDevice)
    {
        using var canvas = new SkiaRenderTarget2D(graphicsDevice, GoldenScene.Width, GoldenScene.Height);
        canvas.Begin();
        canvas.Canvas.Clear(SkiaSharp.SKColors.Magenta);
        canvas.EndWithoutDrawing();

        graphicsDevice.SetRenderTarget(null);
        DrawDirect(graphicsDevice);

        canvas.Begin();
        GoldenScene.Draw(canvas.Canvas);
        canvas.EndWithoutDrawing();
        return ReadRgba(canvas.Texture);
    }

    static void DrawDirect(GraphicsDevice graphicsDevice)
    {
        using var skia = new SkiaDirectCanvas(graphicsDevice);
        skia.Begin();
        GoldenScene.Draw(skia.Canvas);
        skia.End();
    }

    /// <summary>Reads a texture back through the engine's own <c>GetData</c>, as RGBA8888 bytes.</summary>
    internal static byte[] ReadRgba(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        return ToRgba(pixels);
    }

    static byte[] ToRgba(Color[] pixels)
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
}
