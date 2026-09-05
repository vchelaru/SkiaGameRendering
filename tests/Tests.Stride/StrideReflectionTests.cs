using System.Reflection;
using Stride.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.Stride;

/// <summary>
/// Pins the one private Stride field <c>SkiaStrideContext</c> reaches by string
/// (src/SkiaGameRendering.Stride/SkiaStrideContext.cs): <c>GraphicsResourceBase.nativeResource</c>.
///
/// Everything else the Stride adapter needs - <c>GraphicsDevice.NativeDevice</c>,
/// <c>TextureFlags.RenderTarget</c> - is public and used as a normal compile-time member, so a
/// rename there already fails the build; this field is the only place a Stride version bump can
/// break the adapter silently at runtime instead, since it only resolves the field name against a
/// live <c>Texture</c> reflectively.
/// </summary>
public sealed class StrideReflectionTests
{
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void NativeResourceField_Resolves()
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsResourceBase), "nativeResource", NonPublicInstance);

        Assert.True(field.FieldType.IsPointer,
            $"Expected GraphicsResourceBase.nativeResource to be a pointer field, was {field.FieldType}.");
        Assert.Equal("ID3D11Resource", field.FieldType.GetElementType()?.Name);
    }
}
