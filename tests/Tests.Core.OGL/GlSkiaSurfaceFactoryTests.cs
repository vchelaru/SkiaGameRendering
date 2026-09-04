using SkiaGameRendering.Core.OGL;
using Xunit;
using static SkiaGameRendering.Core.OGL.GlConstants;
using static Tests.CoreOgl.RecordingGlFunctionLoader;

namespace Tests.CoreOgl;

/// <summary>
/// Pins the GL call sequence <see cref="GlSkiaSurfaceFactory"/> issues, driven through
/// <see cref="RecordingGlFunctionLoader"/> so no GL context or GPU is involved.
///
/// Order matters here in ways the source does not advertise: an FBO must be bound before its
/// attachments are set, the renderbuffer must be unbound before the FBO is built, and dispose has to
/// detach before it deletes. Getting one of those wrong still compiles and only shows up as a black
/// or corrupt render on real hardware.
///
/// <see cref="GlSkiaSurfaceFactory.CreateSurface"/> itself needs a live <c>GRContext</c>, so these
/// drive <see cref="GlSkiaSurfaceFactory.CreateFramebuffer"/> - its pure-GL half - instead.
/// </summary>
public sealed class GlSkiaSurfaceFactoryTests
{
    private const int TextureId = 7;
    private const int Width = 64;
    private const int Height = 32;

    private readonly RecordingGlFunctionLoader _loader = new();
    private readonly GlFunctions _gl;

    public GlSkiaSurfaceFactoryTests() => _gl = GlFunctions.Load(_loader);

    [Fact]
    public void CreateFramebuffer_IssuesFboSetupSequence()
    {
        GlSkiaSurfaceFactory.CreateFramebuffer(_gl, TextureId, Width, Height, out _);

        Assert.Equal(new[]
        {
            Call("glGetIntegerv", GL_SAMPLES),

            Call("glGenRenderbuffers", 1),
            Call("glBindRenderbuffer", (int)RenderbufferTarget.Renderbuffer, GeneratedRenderbufferId),
            Call("glRenderbufferStorage", (int)RenderbufferTarget.Renderbuffer,
                (int)RenderbufferStorage.Depth24Stencil8, Width, Height),
            Call("glBindRenderbuffer", (int)RenderbufferTarget.Renderbuffer, 0),

            Call("glGenFramebuffers", 1),
            Call("glBindFramebuffer", (int)FramebufferTarget.Framebuffer, GeneratedFramebufferId),
            Call("glFramebufferTexture2D", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.ColorAttachment0, (int)TextureTarget.Texture2D, TextureId, 0),
            Call("glFramebufferRenderbuffer", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.DepthAttachment, (int)RenderbufferTarget.Renderbuffer, GeneratedRenderbufferId),
            Call("glFramebufferRenderbuffer", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.StencilAttachment, (int)RenderbufferTarget.Renderbuffer, GeneratedRenderbufferId),

            Call("glCheckFramebufferStatus", (int)FramebufferTarget.Framebuffer),
            Call("glBindFramebuffer", (int)FramebufferTarget.Framebuffer, 0),
        }, _loader.Calls);
    }

    [Fact]
    public void CreateFramebuffer_ReturnsGeneratedIdsAndSampleCount()
    {
        var state = GlSkiaSurfaceFactory.CreateFramebuffer(_gl, TextureId, Width, Height, out var samples);

        Assert.Equal(GeneratedFramebufferId, state.FramebufferId);
        Assert.Equal(GeneratedRenderbufferId, state.RenderbufferId);
        Assert.Equal(SampleCount, samples);
    }

    /// <summary>
    /// Leaving the new FBO bound would silently redirect the caller's next draw into Skia's texture.
    /// </summary>
    [Fact]
    public void CreateFramebuffer_LeavesDefaultFramebufferBound()
    {
        GlSkiaSurfaceFactory.CreateFramebuffer(_gl, TextureId, Width, Height, out _);

        Assert.Equal(0, _loader.BoundFramebuffer);
    }

    [Fact]
    public void CreateFramebuffer_ThrowsWhenFramebufferIsIncomplete()
    {
        _loader.Status = FramebufferErrorCode.FramebufferIncompleteAttachment;

        Assert.Throws<Exception>(() =>
            GlSkiaSurfaceFactory.CreateFramebuffer(_gl, TextureId, Width, Height, out _));
    }

    [Fact]
    public void BindForDrawing_BindsTheFramebuffer()
    {
        var state = new GlFramebufferState { FramebufferId = GeneratedFramebufferId, RenderbufferId = GeneratedRenderbufferId };

        GlSkiaSurfaceFactory.BindForDrawing(_gl, state);

        Assert.Equal(new[]
        {
            Call("glBindFramebuffer", (int)FramebufferTarget.Framebuffer, GeneratedFramebufferId),
        }, _loader.Calls);
        Assert.Equal(GeneratedFramebufferId, _loader.BoundFramebuffer);
    }

    [Fact]
    public void BindForDrawing_AndUnbind_RestoreTheDefaultFramebuffer()
    {
        var state = GlSkiaSurfaceFactory.CreateFramebuffer(_gl, TextureId, Width, Height, out _);

        GlSkiaSurfaceFactory.BindForDrawing(_gl, state);
        Assert.Equal(state.FramebufferId, _loader.BoundFramebuffer);

        GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);
        Assert.Equal(0, _loader.BoundFramebuffer);
    }

    [Fact]
    public void DisposeRenderState_DetachesBeforeDeletingBothObjects()
    {
        var state = new GlFramebufferState { FramebufferId = GeneratedFramebufferId, RenderbufferId = GeneratedRenderbufferId };

        GlSkiaSurfaceFactory.DisposeRenderState(_gl, state);

        Assert.Equal(new[]
        {
            Call("glBindFramebuffer", (int)FramebufferTarget.Framebuffer, GeneratedFramebufferId),
            Call("glInvalidateFramebuffer", (int)FramebufferTarget.Framebuffer, 3,
                (int)FramebufferAttachment.ColorAttachment0,
                (int)FramebufferAttachment.DepthAttachment,
                (int)FramebufferAttachment.StencilAttachment),
            Call("glFramebufferTexture2D", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.ColorAttachment0, (int)TextureTarget.Texture2D, 0, 0),
            Call("glFramebufferRenderbuffer", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.DepthAttachment, (int)RenderbufferTarget.Renderbuffer, 0),
            Call("glFramebufferRenderbuffer", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.StencilAttachment, (int)RenderbufferTarget.Renderbuffer, 0),
            Call("glBindFramebuffer", (int)FramebufferTarget.Framebuffer, 0),

            Call("glDeleteFramebuffers", 1, GeneratedFramebufferId),
            Call("glDeleteRenderbuffers", 1, GeneratedRenderbufferId),
        }, _loader.Calls);
        Assert.Equal(0, _loader.BoundFramebuffer);
    }

    /// <summary>
    /// The whole round trip: everything created is deleted, and nothing is deleted twice.
    /// </summary>
    [Fact]
    public void DisposeRenderState_DeletesExactlyWhatCreateFramebufferGenerated()
    {
        var state = GlSkiaSurfaceFactory.CreateFramebuffer(_gl, TextureId, Width, Height, out _);
        _loader.Calls.Clear();

        GlSkiaSurfaceFactory.DisposeRenderState(_gl, state);

        Assert.Equal(new[]
        {
            Call("glDeleteFramebuffers", 1, GeneratedFramebufferId),
            Call("glDeleteRenderbuffers", 1, GeneratedRenderbufferId),
        }, _loader.Calls.Where(c => c.StartsWith("glDelete", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Deleting object 0 is a no-op in GL, but binding an FBO of 0 is not - it would leave the
    /// default framebuffer detached mid-frame.
    /// </summary>
    [Fact]
    public void DisposeRenderState_OnUnsetState_IssuesNoCalls()
    {
        GlSkiaSurfaceFactory.DisposeRenderState(_gl, new GlFramebufferState());

        Assert.Empty(_loader.Calls);
    }

    [Fact]
    public void DisposeRenderState_WithOnlyARenderbuffer_DeletesOnlyThat()
    {
        var state = new GlFramebufferState { RenderbufferId = GeneratedRenderbufferId };

        GlSkiaSurfaceFactory.DisposeRenderState(_gl, state);

        Assert.Equal(new[]
        {
            Call("glDeleteRenderbuffers", 1, GeneratedRenderbufferId),
        }, _loader.Calls);
    }
}
