using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Abstractions;
using static SDL3.SDL;

namespace Tests.Fna.OGL;

/// <summary>
/// <see cref="SkiaDirectCanvas"/> on FNA3D's OpenGL driver. See <see cref="DirectDrawLayering"/>.
/// FNA3D's back buffer is the window's default framebuffer only while it matches the window and is not
/// multisampled; otherwise it is FNA3D's own "faux backbuffer" FBO, so both are covered.
/// </summary>
public sealed class FnaOglDirectDrawTests(ITestOutputHelper output)
{
    const string Golden = "fna-ogl-scene.png";
    const int Size = DirectDrawLayering.Size;
    const int GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6;
    const int GL_FRAMEBUFFER = 0x8D40;
    const int GL_COLOR_ATTACHMENT0 = 0x8CE0;
    const int GL_FRAMEBUFFER_ATTACHMENT_OBJECT_NAME = 0x8CD1;

    static FnaOglDirectDrawTests() =>
        Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", "OpenGL");

    [Fact]
    public void SupportsDirectCanvas() =>
        Assert.Equal("True", OneFrameGame.Render(() => new SkiaGlBackend(),
            _ => SkiaRenderer.SupportsDirectCanvas.ToString()));

    [Fact]
    public void BackBuffer_LayersInOrderAndUpright() =>
        NoErrors(OneFrameGame.Render(() => new SkiaGlBackend(), device =>
        {
            device.SetRenderTarget(null);
            if (DrawFramebuffer() != 0)
                return "the back buffer is not the default framebuffer, so this would not test it";
            return DirectDrawLayering.BackBuffer(device);
        }));

    /// <summary>A back buffer larger than the window, or multisampled, makes FNA3D draw into its own FBO.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void FauxBackBuffer_LayersInOrderAndUpright(int multiSampleCount) =>
        NoErrors(OneFrameGame.Render(() => new SkiaGlBackend(),
            device => ResetToFauxBackBuffer(device, multiSampleCount) ?? DrawAndCheckFauxBackBuffer(device)));

    /// <summary>
    /// FNA3D rebuilds the faux backbuffer's renderbuffer in place on a reset, keeping its name, so a
    /// surface cached before the reset looks reusable but is not.
    /// </summary>
    [Fact]
    public void FauxBackBuffer_RebuiltByReset_DrawsIntoTheNewOne() =>
        NoErrors(OneFrameGame.Render(() => new SkiaGlBackend(), device =>
        {
            var error = ResetToFauxBackBuffer(device, 0);
            if (error != null)
                return error;
            var before = DrawAndCheckFauxBackBuffer(device);
            var renderbufferBefore = ColorAttachment();

            error = ResetToFauxBackBuffer(device, 4);
            if (error != null)
                return error;
            if (ColorAttachment() != renderbufferBefore)
                return "FNA3D gave the rebuilt faux backbuffer a new renderbuffer, so this would not test reuse";

            var after = DrawAndCheckFauxBackBuffer(device);
            return before == "" && after == "" ? "" : $"before reset: {before}; after reset: {after}";
        }));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RenderTarget_LayersInOrderAndUpright(int multiSampleCount) =>
        NoErrors(OneFrameGame.Render(() => new SkiaGlBackend(), device =>
        {
            using var renderTarget = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color,
                DepthFormat.None, multiSampleCount, RenderTargetUsage.DiscardContents);
            if (renderTarget.MultiSampleCount != multiSampleCount)
                return $"FNA made a {renderTarget.MultiSampleCount}x target, not {multiSampleCount}x";
            using var other = new RenderTarget2D(device, 1, 1);

            device.SetRenderTarget(renderTarget);
            try
            {
                return DirectDrawLayering.DrawAndCheck(device, () =>
                {
                    // Unbinding resolves a multisampled target. FNA3D's GetData then mistakes the texture
                    // for still bound when its name equals the renderbuffer it last attached, and reads the
                    // multisampled FBO (GL_INVALID_OPERATION, with no Skia drawing at all). Binding another
                    // target in between moves that attachment on.
                    device.SetRenderTarget(null);
                    device.SetRenderTarget(other);
                    device.SetRenderTarget(null);
                    var pixels = new Color[Size * Size];
                    renderTarget.GetData(pixels);
                    return pixels;
                });
            }
            finally
            {
                device.SetRenderTarget(null);
            }
        }));

    [Fact]
    public void Scene_DrawnToRenderTarget_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaGlBackend(),
            EngineSkiaGolden.RenderSceneDirectToRenderTarget, Golden, output);

    [Fact]
    public void Scene_DrawnToBackBuffer_MatchesGolden() =>
        OneFrameGame.RenderAndAssertGolden(() => new SkiaGlBackend(),
            EngineSkiaGolden.RenderSceneDirectToBackBuffer, Golden, output);

    /// <summary>Assert.Equal truncates a long string; every probe failure matters.</summary>
    static void NoErrors(string errors)
    {
        if (errors != "")
            Assert.Fail(errors);
    }

    /// <summary>Twice the window's size, so FNA3D cannot use the window's framebuffer.</summary>
    static string? ResetToFauxBackBuffer(GraphicsDevice device, int multiSampleCount)
    {
        var parameters = device.PresentationParameters.Clone();
        parameters.BackBufferWidth = Size * 2;
        parameters.BackBufferHeight = Size * 2;
        parameters.MultiSampleCount = multiSampleCount;
        device.Reset(parameters);
        device.SetRenderTarget(null);

        if (device.PresentationParameters.MultiSampleCount != multiSampleCount)
            return $"FNA made a {device.PresentationParameters.MultiSampleCount}x back buffer, not {multiSampleCount}x";
        return DrawFramebuffer() == 0 ? "the back buffer is still the default framebuffer" : null;
    }

    /// <summary>
    /// Reads the whole back buffer and probes its top-left corner. FNA3D's OpenGL <c>GetBackBufferData</c>
    /// takes a rectangle's Y from the bottom, so a top-left rectangle would read the bottom-left instead.
    /// </summary>
    static string DrawAndCheckFauxBackBuffer(GraphicsDevice device) =>
        DirectDrawLayering.DrawAndCheck(device, () =>
        {
            var width = device.PresentationParameters.BackBufferWidth;
            var all = new Color[width * device.PresentationParameters.BackBufferHeight];
            device.GetBackBufferData(all);
            var corner = new Color[Size * Size];
            for (var y = 0; y < Size; y++)
                Array.Copy(all, y * width, corner, y * Size, Size);
            return corner;
        });

    delegate void GetIntegervDelegate(int name, out int value);
    delegate void GetFramebufferAttachmentParameterivDelegate(int target, int attachment, int name, out int value);

    /// <summary>The draw framebuffer FNA3D has bound, read on its own (current) context.</summary>
    static int DrawFramebuffer()
    {
        Load<GetIntegervDelegate>("glGetIntegerv")(GL_DRAW_FRAMEBUFFER_BINDING, out var framebuffer);
        return framebuffer;
    }

    static int ColorAttachment()
    {
        Load<GetFramebufferAttachmentParameterivDelegate>("glGetFramebufferAttachmentParameteriv")(
            GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_NAME, out var name);
        return name;
    }

    static T Load<T>(string name) where T : Delegate
    {
        var address = SDL_GL_GetProcAddress(name);
        Assert.NotEqual(IntPtr.Zero, address);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
}
