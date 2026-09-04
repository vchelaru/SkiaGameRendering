using System.Reflection;
using Microsoft.Xna.Platform.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.Kni.WindowsDX;

/// <summary>
/// Pins the internal KNI strategy members <c>SkiaKniAngleBackend</c> reaches by string
/// (src/SkiaGameRendering.Kni.WindowsDX/SkiaKniAngleBackend.cs). KNI's Strategy bridge is public down
/// to the concrete class, but the last hop to the SharpDX device, context and texture is not, so a
/// KNI bump renaming one leaves the build green and breaks every Skia draw.
///
/// The backend reaches the concrete types through <c>strategy.GetType()</c> on a live device, and
/// never spells their names, so these resolve them the same structural way: every concrete
/// implementation of the strategy type in Kni.Platform. Hardcoding "ConcreteGraphicsDevice" would
/// fail on a rename the backend does not care about.
/// </summary>
public sealed class KniWindowsDxReflectionTests
{
    static readonly Assembly KniPlatform = EngineReflectionPin.RequireAssembly("Kni.Platform");

    const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The D3D11 device AngleSkiaSurfaceFactory is initialized with.</summary>
    [Fact]
    public void GraphicsDeviceStrategyD3DDevice_Resolves()
    {
        foreach (var strategy in ConcreteImplementationsOf(typeof(IPlatformGraphicsDevice)))
            EngineReflectionPin.RequireProperty(strategy, "D3DDevice", NonPublicInstance);
    }

    /// <summary>The D3D11 immediate context, whose state ANGLE saves and restores around each draw.</summary>
    [Fact]
    public void GraphicsContextStrategyD3dContext_Resolves()
    {
        foreach (var strategy in ConcreteImplementationsOf(typeof(IPlatformGraphicsContext)))
            EngineReflectionPin.RequireProperty(strategy, "D3dContext", NonPublicInstance);
    }

    /// <summary>The SharpDX resource behind a render target, shared into ANGLE without a copy.</summary>
    [Fact]
    public void TextureStrategyGetTexture_Resolves()
    {
        foreach (var strategy in EngineReflectionPin.RequireImplementations(KniPlatform, typeof(ITexture2DStrategy)))
            EngineReflectionPin.RequireMethod(strategy, "GetTexture", NonPublicInstance);
    }

    /// <summary>
    /// The strategy types are only named by the Strategy property on KNI's platform interfaces, so
    /// the pin reads the type off the interface rather than repeating a name the backend never uses.
    /// </summary>
    static IReadOnlyList<Type> ConcreteImplementationsOf(Type platformInterface)
    {
        var strategyType = EngineReflectionPin.RequireProperty(platformInterface, "Strategy", PublicInstance).PropertyType;
        return EngineReflectionPin.RequireImplementations(KniPlatform, strategyType);
    }
}
