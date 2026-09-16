using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.Fna.OGL;

/// <summary>
/// Pins the private FNA fields <c>SkiaFnaGlBackend</c> reaches by string
/// (src/SkiaGameRendering.Fna.OGL/SkiaFnaGlBackend.cs). Same two members as
/// <c>Tests.Fna.WindowsDX</c> pins for the D3D11 adapter; the FNA side of the interop is
/// driver-agnostic. What this can't pin is the OpenGLTexture struct layout inside FNA3D.dll that
/// <c>CaptureTextureHandle</c> reads through; <see cref="FnaOglGoldenImageTests"/> exercises that.
/// </summary>
public sealed class FnaOglReflectionTests
{
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The FNA3D_Device* handed to FNA3D_GetSysRendererEXT.</summary>
    [Fact]
    public void GraphicsDeviceGLDeviceField_ResolvesAsIntPtr()
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsDevice), "GLDevice", NonPublicInstance);
        Assert.Equal(typeof(IntPtr), field.FieldType);
    }

    /// <summary>The FNA3D_Texture* behind a Texture2D, whose first field is the GL texture name Skia renders into.</summary>
    [Fact]
    public void TextureHandleField_ResolvesAsIntPtr()
    {
        var field = EngineReflectionPin.RequireField(typeof(Texture), "texture", NonPublicInstance);
        Assert.Equal(typeof(IntPtr), field.FieldType);
    }
}
