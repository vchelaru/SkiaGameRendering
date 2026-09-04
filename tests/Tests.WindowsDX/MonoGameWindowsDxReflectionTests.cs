using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.WindowsDX;

/// <summary>
/// Pins the private MonoGame WindowsDX fields <c>SkiaAngleBackend</c> reaches by string
/// (src/SkiaGameRendering.WindowsDX/SkiaAngleBackend.cs). These hold the SharpDX wrappers ANGLE is
/// handed, so a MonoGame bump that renames one turns every Skia draw into a black texture with a
/// green build behind it.
///
/// The names are duplicated from the backend on purpose - it only resolves them while initializing
/// against a live GraphicsDevice, which needs a D3D11 device and a window.
/// </summary>
public sealed class MonoGameWindowsDxReflectionTests
{
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The D3D11 device and immediate context AngleSkiaSurfaceFactory is initialized with.</summary>
    [Fact]
    public void GraphicsDeviceD3DFields_Resolve()
    {
        EngineReflectionPin.RequireField(typeof(GraphicsDevice), "_d3dDevice", NonPublicInstance);
        EngineReflectionPin.RequireField(typeof(GraphicsDevice), "_d3dContext", NonPublicInstance);
    }

    /// <summary>The SharpDX resource behind a Texture2D, shared into ANGLE without a copy.</summary>
    [Fact]
    public void TextureResourceField_Resolves()
    {
        EngineReflectionPin.RequireField(typeof(Texture), "_texture", NonPublicInstance);
    }
}
