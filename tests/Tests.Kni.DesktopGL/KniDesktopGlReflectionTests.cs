using System.Reflection;
using Tests.Shared;
using Xunit;

namespace Tests.Kni.DesktopGL;

/// <summary>
/// Pins the KNI SDL2 GL platform members <c>KniGlWrapper</c> reaches by string
/// (src/SkiaGameRendering.Kni.DesktopGL/KniGLUtils.cs). They are public on KNI's side, but they live
/// in Kni.Platform, which the backend package does not reference - it finds the assembly at runtime -
/// so nothing in the build sees a rename.
///
/// The names are duplicated from the backend on purpose: it resolves them inside a static constructor
/// that also reads Sdl.Current, which is null until KNI has created a window.
/// </summary>
public sealed class KniDesktopGlReflectionTests
{
    static readonly Assembly KniPlatform = EngineReflectionPin.RequireAssembly("Kni.Platform");

    const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    static Type SdlType => EngineReflectionPin.RequireType(KniPlatform, "Sdl");
    static Type SdlGlType => EngineReflectionPin.RequireNestedType(SdlType, "GL");

    /// <summary>KniGlWrapper walks Sdl.Current to Sdl.OpenGL to reach the GL instance.</summary>
    [Fact]
    public void SdlGlInstanceChain_Resolves()
    {
        EngineReflectionPin.RequireProperty(SdlType, "Current", BindingFlags.Public | BindingFlags.Static);
        EngineReflectionPin.RequireProperty(SdlType, "OpenGL", PublicInstance);
    }

    /// <summary>The GL methods invoked with a fixed-length object[], so arity is pinned too.</summary>
    [Fact]
    public void SdlGlMethods_Resolve()
    {
        EngineReflectionPin.RequireParameterCount(
            EngineReflectionPin.RequireMethod(SdlGlType, "GetCurrentContext", PublicInstance), 0);
        EngineReflectionPin.RequireParameterCount(
            EngineReflectionPin.RequireMethod(SdlGlType, "CreateGLContext", PublicInstance), 1);
        EngineReflectionPin.RequireParameterCount(
            EngineReflectionPin.RequireMethod(SdlGlType, "SetAttribute", PublicInstance), 2);
    }

    /// <summary>The GL entry points held as delegate fields rather than methods.</summary>
    [Fact]
    public void SdlGlDelegateFields_Resolve()
    {
        EngineReflectionPin.RequireDelegateField(SdlGlType, "MakeCurrent", PublicInstance, parameterCount: 2);
        EngineReflectionPin.RequireDelegateField(SdlGlType, "GetProcAddress", PublicInstance, parameterCount: 1);
    }

    /// <summary>
    /// The shared-context attribute KniGlWrapper passes to SetAttribute. It is parsed from the name,
    /// so dropping the member is a runtime ArgumentException.
    /// </summary>
    [Fact]
    public void ShareWithCurrentContextAttribute_Resolves()
    {
        var attributeEnum = EngineReflectionPin.RequireNestedType(SdlGlType, "Attribute");

        Assert.True(attributeEnum.IsEnum, $"'{attributeEnum.FullName}' is no longer an enum.");
        Assert.Contains("ShareWithCurrentContext", Enum.GetNames(attributeEnum));
    }
}
