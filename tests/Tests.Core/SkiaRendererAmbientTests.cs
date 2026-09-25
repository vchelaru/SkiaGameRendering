using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Xunit;
using FakeBackend = Tests.Core.SkiaRendererTests.FakeBackend;

namespace Tests.Core;

/// <summary>
/// Covers <see cref="SkiaRenderer.IsReady"/> and the ambient init path (<see
/// cref="SkiaRenderer.AttachAmbient"/>), which <see cref="SkiaRendererTests"/> doesn't touch because
/// it only exercises the two-arg <c>Initialize(SkiaBackend, GraphicsDevice)</c> overload.
/// </summary>
[Collection("SkiaRenderer static state")]
public sealed class SkiaRendererAmbientTests : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice =
        (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

    public SkiaRendererAmbientTests()
    {
        SkiaRenderer.Dispose();
        SkiaRenderer.AttachAmbient(null, null);
    }

    [Fact]
    public void IsReady_IsTrueByDefaultWithNothingAttached()
    {
        Assert.True(SkiaRenderer.IsReady);
    }

    [Fact]
    public void IsReady_ReflectsAttachedReadyCheckUntilItFlips()
    {
        var ready = false;
        SkiaRenderer.AttachAmbient(() => new FakeBackend(), () => ready);

        Assert.False(SkiaRenderer.IsReady);

        ready = true;

        Assert.True(SkiaRenderer.IsReady);
    }

    [Fact]
    public void Initialize_UsesAmbientFactoryInsteadOfReflection()
    {
        var backend = new FakeBackend();
        SkiaRenderer.AttachAmbient(() => backend, () => true);

        SkiaRenderer.Initialize(_graphicsDevice);

        Assert.Same(backend, SkiaRenderer.CurrentBackend);
        Assert.Equal(1, backend.InitializeCount);
    }

    [Fact]
    public void CurrentBackend_IsNullBeforeInitAndAfterDispose()
    {
        Assert.Null(SkiaRenderer.CurrentBackend);

        var backend = new FakeBackend();
        SkiaRenderer.Initialize(backend, _graphicsDevice);

        Assert.Same(backend, SkiaRenderer.CurrentBackend);

        SkiaRenderer.Dispose();

        Assert.Null(SkiaRenderer.CurrentBackend);
    }

    [Fact]
    public void RecommendedPollPattern_InitializesExactlyOnceOnceReady()
    {
        const int pollsBeforeReady = 3;
        var pollCount = 0;
        var backend = new FakeBackend();
        SkiaRenderer.AttachAmbient(() => backend, () => ++pollCount > pollsBeforeReady);

        for (var i = 0; i < pollsBeforeReady + 5; i++)
        {
            if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
                SkiaRenderer.Initialize(_graphicsDevice);
        }

        Assert.Equal(1, backend.InitializeCount);
        Assert.Same(backend, SkiaRenderer.CurrentBackend);
    }

    [Fact]
    public void Dispose_LeavesAmbientDelegatesAttachedSoReinitializeReusesThem()
    {
        var firstBackend = new FakeBackend();
        var secondBackend = new FakeBackend();
        var backends = new Queue<FakeBackend>(new[] { firstBackend, secondBackend });
        SkiaRenderer.AttachAmbient(() => backends.Dequeue(), () => true);

        SkiaRenderer.Initialize(_graphicsDevice);
        Assert.Same(firstBackend, SkiaRenderer.CurrentBackend);

        SkiaRenderer.Dispose();
        Assert.Null(SkiaRenderer.CurrentBackend);

        SkiaRenderer.Initialize(_graphicsDevice);

        Assert.Same(secondBackend, SkiaRenderer.CurrentBackend);
        Assert.Equal(1, secondBackend.InitializeCount);
    }

    [Fact]
    public void CreateDefaultBackend_IsThisPackagesBackend()
    {
        // Initialize(GraphicsDevice) with no ambient factory uses this. Asserted directly rather than
        // through Initialize, since initializing a real SkiaGlBackend needs a live GL context.
        Assert.IsType<SkiaGlBackend>(SkiaRenderer.CreateDefaultBackend());
    }

    public void Dispose()
    {
        SkiaRenderer.Dispose();
        SkiaRenderer.AttachAmbient(null, null);
    }
}
