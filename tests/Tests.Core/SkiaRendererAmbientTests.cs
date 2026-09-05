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
///
/// Reuses <see cref="SkiaRendererTests.FakeBackend"/> rather than declaring a second concrete
/// <c>SkiaBackend</c> subclass here: <c>SkiaRenderer.FindBackendType()</c> scans every assembly in
/// the AppDomain for any non-abstract SkiaBackend subclass, so a second one loaded anywhere in this
/// test process would make the reflection-fallback test's outcome depend on enumeration order (see
/// that test's comment, and this PR's description, for why no second type was added).
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
    public void Initialize_WithNoAmbientFactoryFallsThroughToReflectionAndConstructsTheDetectedBackend()
    {
        // No ambient factory attached (constructor reset it). FakeBackend is the only concrete
        // SkiaBackend subclass loaded in this test process, so FindBackendType() deterministically
        // finds it and Activator.CreateInstance constructs it via its implicit parameterless ctor.
        //
        // This does NOT exercise the "no public parameterless constructor -> friendly
        // InvalidOperationException, not a raw MissingMethodException" branch a few lines below in
        // SkiaRenderer.Initialize(GraphicsDevice) - see this PR's description for why: on .NET 8,
        // Activator.CreateInstance can construct a private (or internal) nested type's implicit
        // parameterless constructor from a different assembly without throwing, so a type like this
        // one can't be used to exercise that branch. Reaching it would need a second concrete
        // SkiaBackend type with NO parameterless constructor at all (e.g. one requiring a host
        // object, like the real SkiaWebGlBackend) - which reintroduces exactly the enumeration-order
        // ambiguity called out above, since FindBackendType() would then have two matching types and
        // might construct either one. Skipped rather than risk a flaky test.
        SkiaRenderer.Initialize(_graphicsDevice);

        var backend = Assert.IsType<FakeBackend>(SkiaRenderer.CurrentBackend);
        Assert.Equal(1, backend.InitializeCount);
    }

    public void Dispose()
    {
        SkiaRenderer.Dispose();
        SkiaRenderer.AttachAmbient(null, null);
    }
}
