using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Xunit;
using FakeBackend = Tests.Core.SkiaRendererTests.FakeBackend;

namespace Tests.Core;

/// <summary>The Begin/Canvas/End guards of <see cref="SkiaDirectCanvas"/>, the same ones <see cref="SkiaRenderTarget2D"/> has.</summary>
[Collection("SkiaRenderer static state")]
public sealed class SkiaDirectCanvasTests : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice =
        (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

    public SkiaDirectCanvasTests() => SkiaRenderer.Dispose();

    public void Dispose() => SkiaRenderer.Dispose();

    private FakeBackend InitializeSupported()
    {
        var backend = new FakeBackend { DirectCanvasSupported = true };
        SkiaRenderer.Initialize(backend, _graphicsDevice);
        return backend;
    }

    [Fact]
    public void Constructing_ThrowsNotSupportedWhereTheBackendCannotDrawDirect()
    {
        SkiaRenderer.Initialize(new FakeBackend(), _graphicsDevice);

        Assert.False(SkiaRenderer.SupportsDirectCanvas);
        Assert.Throws<NotSupportedException>(() => new SkiaDirectCanvas(_graphicsDevice));
    }

    [Fact]
    public void Constructing_WithDifferentGraphicsDeviceThanInitializedThrows()
    {
        InitializeSupported();
        var otherDevice = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

        Assert.Throws<InvalidOperationException>(() => new SkiaDirectCanvas(otherDevice));
    }

    [Fact]
    public void Canvas_ThrowsOutsideBeginEndWindow()
    {
        InitializeSupported();
        using var skia = new SkiaDirectCanvas(_graphicsDevice);

        Assert.True(SkiaRenderer.SupportsDirectCanvas);
        Assert.Throws<InvalidOperationException>(() => skia.Canvas);

        skia.Begin();
        Assert.NotNull(skia.Canvas);
        skia.End();

        Assert.Throws<InvalidOperationException>(() => skia.Canvas);
    }

    [Fact]
    public void Begin_ThrowsIfCalledAgainBeforeEnd()
    {
        var backend = InitializeSupported();
        using var skia = new SkiaDirectCanvas(_graphicsDevice);

        skia.Begin();
        Assert.Throws<InvalidOperationException>(skia.Begin);
        skia.End();

        Assert.Equal(1, backend.BeginDirectDrawCount);
    }

    [Fact]
    public void End_ThrowsIfBeginWasNotCalled()
    {
        var backend = InitializeSupported();
        using var skia = new SkiaDirectCanvas(_graphicsDevice);

        Assert.Throws<InvalidOperationException>(skia.End);
        Assert.Equal(0, backend.EndDirectDrawCount);
    }

    [Fact]
    public void Begin_DoesNotLockOutRetryWhenItThrows()
    {
        var backend = InitializeSupported();
        using var skia = new SkiaDirectCanvas(_graphicsDevice);

        backend.BeginDirectDrawException = new InvalidOperationException("expected");
        Assert.Throws<InvalidOperationException>(skia.Begin);
        Assert.Throws<InvalidOperationException>(() => skia.Canvas);

        backend.BeginDirectDrawException = null;
        skia.Begin();
        skia.End();

        Assert.Equal(2, backend.BeginDirectDrawCount);
        Assert.Equal(1, backend.EndDirectDrawCount);
    }

    [Fact]
    public void End_DoesNotLockOutLaterPassesWhenItThrows()
    {
        var backend = InitializeSupported();
        using var skia = new SkiaDirectCanvas(_graphicsDevice);

        skia.Begin();
        backend.EndDirectDrawException = new InvalidOperationException("expected");
        Assert.Throws<InvalidOperationException>(skia.End);
        Assert.Throws<InvalidOperationException>(() => skia.Canvas);

        backend.EndDirectDrawException = null;
        skia.Begin();
        skia.End();

        Assert.Equal(2, backend.EndDirectDrawCount);
    }

    [Fact]
    public void Dispose_ThrowsBetweenBeginAndEnd_AndBeginThrowsAfterDispose()
    {
        InitializeSupported();
        var skia = new SkiaDirectCanvas(_graphicsDevice);

        skia.Begin();
        Assert.Throws<InvalidOperationException>(skia.Dispose);
        skia.End();

        skia.Dispose();
        skia.Dispose();
        Assert.Throws<ObjectDisposedException>(skia.Begin);
    }
}
