using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;
using Xunit;

namespace Tests.Core;

// SkiaRenderer holds process-wide static state, so this collection also covers
// SkiaRendererAmbientTests - xUnit runs different collections in parallel by default, and the two
// classes would otherwise race on that shared state.
[CollectionDefinition("SkiaRenderer static state")]
public sealed class SkiaRendererCollection { }

[Collection("SkiaRenderer static state")]
public sealed class SkiaRendererTests : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice =
        (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

    public SkiaRendererTests() => SkiaRenderer.Dispose();

    [Fact]
    public void Initialize_IsIdempotentOnlyForSameConfiguration()
    {
        var backend = new FakeBackend();

        SkiaRenderer.Initialize(backend, _graphicsDevice);
        SkiaRenderer.Initialize(backend, _graphicsDevice);

        Assert.Equal(1, backend.InitializeCount);
        Assert.Throws<InvalidOperationException>(() =>
            SkiaRenderer.Initialize(new FakeBackend(), _graphicsDevice));
    }

    [Fact]
    public void Constructing_AutoInitializesAndCreatesTargetEagerly()
    {
        var backend = new FakeBackend();
        SkiaRenderer.Initialize(backend, _graphicsDevice);

        using var target = new SkiaRenderTarget2D(_graphicsDevice, 32, 32);

        Assert.Equal(1, backend.InitializeCount);
        Assert.Single(backend.CreatedTargets);
        Assert.Same(backend.CreatedTargets[0].Texture, target.Texture);
    }

    [Fact]
    public void Constructing_WithDifferentGraphicsDeviceThanInitializedThrows()
    {
        SkiaRenderer.Initialize(new FakeBackend(), _graphicsDevice);
        var otherDevice = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

        Assert.Throws<InvalidOperationException>(() => new SkiaRenderTarget2D(otherDevice, 32, 32));
    }

    [Fact]
    public void Canvas_ThrowsOutsideBeginEndWindow()
    {
        var backend = new FakeBackend();
        SkiaRenderer.Initialize(backend, _graphicsDevice);
        using var target = new SkiaRenderTarget2D(_graphicsDevice, 32, 32);

        Assert.Throws<InvalidOperationException>(() => target.Canvas);

        target.Begin();
        Assert.NotNull(target.Canvas);
        target.EndWithoutDrawing();

        Assert.Throws<InvalidOperationException>(() => target.Canvas);
    }

    [Fact]
    public void Begin_ThrowsIfCalledAgainBeforeEnd()
    {
        var backend = new FakeBackend();
        SkiaRenderer.Initialize(backend, _graphicsDevice);
        using var target = new SkiaRenderTarget2D(_graphicsDevice, 32, 32);

        target.Begin();

        Assert.Throws<InvalidOperationException>(() => target.Begin());

        target.EndWithoutDrawing();
    }

    [Fact]
    public void End_ThrowsIfBeginWasNotCalled()
    {
        var backend = new FakeBackend();
        SkiaRenderer.Initialize(backend, _graphicsDevice);
        using var target = new SkiaRenderTarget2D(_graphicsDevice, 32, 32);

        Assert.Throws<InvalidOperationException>(() => target.End());
    }

    [Fact]
    public void Begin_DoesNotLockOutRetryWhenBeginRenderThrows()
    {
        var backend = new FakeBackend();
        SkiaRenderer.Initialize(backend, _graphicsDevice);
        using var target = new SkiaRenderTarget2D(_graphicsDevice, 32, 32);

        backend.BeginRenderException = new InvalidOperationException("expected");
        Assert.Throws<InvalidOperationException>(() => target.Begin());

        backend.BeginRenderException = null;
        target.Begin();
        target.EndWithoutDrawing();

        Assert.Equal(2, backend.BeginRenderCount);
        Assert.Equal(1, backend.EndRenderCount);
    }

    [Fact]
    public void Dispose_ThrowsBetweenBeginAndEndThenSucceedsAfterEnd()
    {
        var backend = new FakeBackend();
        SkiaRenderer.Initialize(backend, _graphicsDevice);
        var target = new SkiaRenderTarget2D(_graphicsDevice, 32, 32);

        target.Begin();
        Assert.Throws<InvalidOperationException>(target.Dispose);

        target.EndWithoutDrawing();
        target.Dispose();

        Assert.Equal(1, backend.CreatedTargets[0].SkiaDisposeCount);
        Assert.Equal(1, backend.CreatedTargets[0].GraphicsDisposeCount);
    }

    [Fact]
    public void SkiaRenderer_Dispose_TearsDownBackend()
    {
        var backend = new FakeBackend();
        SkiaRenderer.Initialize(backend, _graphicsDevice);

        SkiaRenderer.Dispose();

        Assert.True(backend.IsDisposed);
        Assert.False(SkiaRenderer.IsInitialized);
    }

    public void Dispose() => SkiaRenderer.Dispose();

    // Internal, not private: SkiaRendererAmbientTests.cs reuses this as the sole concrete
    // SkiaBackend subclass loaded in this test process - see the scoping note there about why a
    // second one isn't added. (Internal vs. private makes no difference to Activator.CreateInstance
    // across assemblies on .NET 8 - neither blocks reflection construction of this type - so the
    // visibility bump doesn't change anything the existing tests below rely on.)
    internal sealed class FakeBackend : SkiaBackend
    {
        public int InitializeCount { get; private set; }
        public int BeginRenderCount { get; private set; }
        public int EndRenderCount { get; private set; }
        public bool IsDisposed { get; private set; }
        public Exception? BeginRenderException { get; set; }
        public List<FakeTarget> CreatedTargets { get; } = new();
        public override GRContext GRContext => null!;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;
            InitializeCount++;
        }

        internal override void BeginDraw() { }
        internal override void EndDraw() { }

        internal override SkiaTarget CreateTarget(int width, int height, SKColorType colorType)
        {
            var target = new FakeTarget();
            CreatedTargets.Add(target);
            return target;
        }

        internal override SKCanvas BeginRender(SkiaTarget target, bool clear)
        {
            BeginRenderCount++;
            if (BeginRenderException != null)
                throw BeginRenderException;
            return ((FakeTarget)target).Canvas;
        }

        internal override void EndRender(SkiaTarget target) => EndRenderCount++;

        internal override object CaptureTextureHandle(Texture2D texture) => throw new NotSupportedException();
        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState) =>
            throw new NotSupportedException();
        internal override void BindForDrawing(object renderState) => throw new NotSupportedException();
        internal override void UnbindAfterDrawing() => throw new NotSupportedException();
        internal override void DisposeRenderState(object renderState) => throw new NotSupportedException();

        public override void Dispose() => IsDisposed = true;
    }

    internal sealed class FakeTarget : SkiaTarget
    {
        private static readonly Texture2D DummyTexture =
            (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        private readonly SKSurface _surface = SKSurface.Create(new SKImageInfo(1, 1));

        public int SkiaDisposeCount { get; private set; }
        public int GraphicsDisposeCount { get; private set; }
        public override Texture2D Texture => DummyTexture;
        internal SKCanvas Canvas => _surface.Canvas;

        internal override void DisposeSkiaResources()
        {
            SkiaDisposeCount++;
            _surface.Dispose();
        }

        internal override void DisposeGraphicsResources() => GraphicsDisposeCount++;
    }
}
