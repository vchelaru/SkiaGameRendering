using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.Fna.WindowsDX;

/// <summary>
/// Pins the private FNA fields <c>SkiaFnaAngleBackend</c> reaches by string
/// (src/SkiaGameRendering.Fna.WindowsDX/SkiaFnaAngleBackend.cs). Both are raw FNA3D pointers, so
/// an FNA bump that renames or retypes one turns every Skia draw into an exception (or a black
/// texture) behind a green build.
///
/// The names are duplicated from the backend on purpose - it only resolves them while initializing
/// against a live GraphicsDevice, which needs an FNA window and an FNA3D device.
///
/// What this can't pin: the D3D11Texture struct layout inside FNA3D.dll that
/// <c>CaptureTextureHandle</c> reads through. <see cref="FnaWindowsDxGoldenImageTests"/> is what
/// exercises that for real.
/// </summary>
public sealed class FnaWindowsDxReflectionTests
{
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The FNA3D_Device* handed to FNA3D_GetSysRendererEXT.</summary>
    [Fact]
    public void GraphicsDeviceGLDeviceField_ResolvesAsIntPtr()
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsDevice), "GLDevice", NonPublicInstance);
        Assert.Equal(typeof(IntPtr), field.FieldType);
    }

    /// <summary>The FNA3D_Texture* behind a Texture2D, whose first field is the D3D11 resource ANGLE shares.</summary>
    [Fact]
    public void TextureHandleField_ResolvesAsIntPtr()
    {
        var field = EngineReflectionPin.RequireField(typeof(Texture), "texture", NonPublicInstance);
        Assert.Equal(typeof(IntPtr), field.FieldType);
    }
}
