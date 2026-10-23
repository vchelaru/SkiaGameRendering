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

    // A bind of the texture that is already bound is a no-op to the driver, so it has to be unbound
    // first; and the engine's own binding must be put back, or its texture cache would be wrong.
    [Fact]
    public void RefreshSharedTexture_BindsNothingThenTheTexture_ThenRestoresThePreviousBinding()
    {
        GlSkiaSurfaceFactory.RefreshSharedTexture(_gl, TextureId);

        const int Texture2DBindingQuery = 0x8069;
        const int Texture2D = 0x0DE1;
        // The recording loader answers every glGetIntegerv with SampleCount, which stands in for the previous binding.
        Assert.Equal(new[]
        {
            Call("glGetIntegerv", Texture2DBindingQuery),
            Call("glBindTexture", Texture2D, 0),
            Call("glBindTexture", Texture2D, TextureId),
            Call("glBindTexture", Texture2D, SampleCount),
        }, _loader.Calls);
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

    /// <summary>
    /// The engine samples the texture from its own shared context, which only sees this context's
    /// writes after a flush. Without it macOS composites a blank texture.
    /// </summary>
    [Fact]
    public void UnbindAfterDrawing_FlushesAfterUnbinding()
    {
        GlSkiaSurfaceFactory.UnbindAfterDrawing(_gl);

        Assert.Equal(new[]
        {
            Call("glBindFramebuffer", (int)FramebufferTarget.Framebuffer, 0),
            Call("glFlush"),
        }, _loader.Calls);
    }

    /// <summary>glInvalidateFramebuffer is GL 4.3; macOS stops at 4.1.</summary>
    [Fact]
    public void DisposeRenderState_WithoutInvalidateFramebuffer_StillDeletesBothObjects()
    {
        var loader = new RecordingGlFunctionLoader();
        loader.Missing.Add("glInvalidateFramebuffer");
        var gl = GlFunctions.Load(loader);
        var state = new GlFramebufferState { FramebufferId = GeneratedFramebufferId, RenderbufferId = GeneratedRenderbufferId };

        GlSkiaSurfaceFactory.DisposeRenderState(gl, state);

        Assert.DoesNotContain(loader.Calls, call => call.StartsWith("glInvalidateFramebuffer"));
        Assert.Contains(Call("glDeleteFramebuffers", 1, GeneratedFramebufferId), loader.Calls);
        Assert.Contains(Call("glDeleteRenderbuffers", 1, GeneratedRenderbufferId), loader.Calls);
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

    [Fact]
    public void QueryDrawTarget_ReadsTheBoundFramebuffersColorAttachment()
    {
        _loader.AttachmentParameters[GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE] = GL_RENDERBUFFER;
        _loader.AttachmentParameters[GL_FRAMEBUFFER_ATTACHMENT_OBJECT_NAME] = 9;

        var target = GlSkiaSurfaceFactory.QueryDrawTarget(_gl);

        // The recording loader answers every glGetIntegerv with SampleCount, which stands in for the bound FBO.
        Assert.Equal(new GlDrawTarget(SampleCount, GL_RENDERBUFFER, 9), target);
        Assert.False(target.IsDefaultFramebuffer);
        Assert.False(target.IsTexture);

        // GL_DRAW_FRAMEBUFFER is an invalid target for this query on GLES 2.
        Assert.Contains(Call("glGetFramebufferAttachmentParameteriv", (int)FramebufferTarget.Framebuffer,
            GL_COLOR_ATTACHMENT0, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE), _loader.Calls);
    }

    /// <summary>
    /// A multisampled engine target draws into a renderbuffer that the engine resolves into its texture
    /// when the target is unbound. Skia has to draw into that renderbuffer, at its sample count, or the
    /// resolve overwrites what Skia drew.
    /// </summary>
    [Fact]
    public void CreateAttachmentFramebuffer_ForRenderbuffer_AttachesItWithMatchingDepthStencilSamples()
    {
        const int engineRenderbuffer = 9;

        var state = GlSkiaSurfaceFactory.CreateAttachmentFramebuffer(_gl,
            new GlDrawTarget(5, GL_RENDERBUFFER, engineRenderbuffer), Width, Height, out var samples);

        Assert.Equal(SampleCount, samples);
        Assert.Equal(GeneratedFramebufferId, state.FramebufferId);
        Assert.Equal(new[]
        {
            Call("glBindRenderbuffer", (int)RenderbufferTarget.Renderbuffer, engineRenderbuffer),
            Call("glGetRenderbufferParameteriv", (int)RenderbufferTarget.Renderbuffer, GL_RENDERBUFFER_SAMPLES),
            Call("glBindRenderbuffer", (int)RenderbufferTarget.Renderbuffer, 0),

            Call("glGenRenderbuffers", 1),
            Call("glBindRenderbuffer", (int)RenderbufferTarget.Renderbuffer, GeneratedRenderbufferId),
            Call("glRenderbufferStorageMultisample", (int)RenderbufferTarget.Renderbuffer, SampleCount,
                (int)RenderbufferStorage.Depth24Stencil8, Width, Height),
            Call("glBindRenderbuffer", (int)RenderbufferTarget.Renderbuffer, 0),

            Call("glGenFramebuffers", 1),
            Call("glBindFramebuffer", (int)FramebufferTarget.Framebuffer, GeneratedFramebufferId),
            Call("glFramebufferRenderbuffer", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.ColorAttachment0, (int)RenderbufferTarget.Renderbuffer, engineRenderbuffer),
            Call("glFramebufferRenderbuffer", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.DepthAttachment, (int)RenderbufferTarget.Renderbuffer, GeneratedRenderbufferId),
            Call("glFramebufferRenderbuffer", (int)FramebufferTarget.Framebuffer,
                (int)FramebufferAttachment.StencilAttachment, (int)RenderbufferTarget.Renderbuffer, GeneratedRenderbufferId),

            Call("glCheckFramebufferStatus", (int)FramebufferTarget.Framebuffer),
            Call("glBindFramebuffer", (int)FramebufferTarget.Framebuffer, 0),
        }, _loader.Calls);
    }

    [Fact]
    public void CreateAttachmentFramebuffer_ForTexture_IsSingleSampled()
    {
        GlSkiaSurfaceFactory.CreateAttachmentFramebuffer(_gl,
            new GlDrawTarget(5, GL_TEXTURE, TextureId), Width, Height, out var samples);

        Assert.Equal(0, samples);
        Assert.Contains(Call("glFramebufferTexture2D", (int)FramebufferTarget.Framebuffer,
            (int)FramebufferAttachment.ColorAttachment0, (int)TextureTarget.Texture2D, TextureId, 0), _loader.Calls);
        Assert.Contains(Call("glRenderbufferStorage", (int)RenderbufferTarget.Renderbuffer,
            (int)RenderbufferStorage.Depth24Stencil8, Width, Height), _loader.Calls);
    }

    [Fact]
    public void CreateAttachmentFramebuffer_WhenIncomplete_DeletesWhatItMadeAndThrows()
    {
        _loader.Status = FramebufferErrorCode.FramebufferUnsupported;

        Assert.Throws<InvalidOperationException>(() => GlSkiaSurfaceFactory.CreateAttachmentFramebuffer(_gl,
            new GlDrawTarget(5, GL_TEXTURE, TextureId), Width, Height, out _));

        Assert.Contains(Call("glDeleteFramebuffers", 1, GeneratedFramebufferId), _loader.Calls);
        Assert.Contains(Call("glDeleteRenderbuffers", 1, GeneratedRenderbufferId), _loader.Calls);
        Assert.Equal(0, _loader.BoundFramebuffer);
    }
}
