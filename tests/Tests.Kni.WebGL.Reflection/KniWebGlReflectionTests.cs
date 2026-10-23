using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform.Graphics;
using SkiaGameRendering.Kni.WebGL;
using Tests.Shared;
using Xunit;

namespace Tests.Kni.WebGL;

/// <summary>
/// Pins the internal KNI and nkast.Wasm.Canvas members <c>KniWebGlInternals</c> reaches by string
/// (src/SkiaGameRendering.Kni.WebGL/WebGlCanvasUpload.cs). None of it is compile-checked, so a bump
/// of either <c>KniVersion</c> or <c>NkastWasmCanvasVersion</c> that renames one leaves the build
/// green and throws the first time a game calls <c>UploadFromCanvas</c>.
///
/// The backend reaches the strategy and nkast.Wasm types through <c>GetType()</c> on live objects
/// and never spells their names, so these resolve them the same structural way rather than
/// hardcoding a name the backend does not care about.
/// </summary>
public sealed class KniWebGlReflectionTests
{
    static readonly Assembly KniPlatform = EngineReflectionPin.RequireAssembly("Kni.Platform");
    static readonly Assembly NkastWasmCanvas = EngineReflectionPin.RequireAssembly("nkast.Wasm.Canvas");

    const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The hop from the public Texture2D to the platform strategy holding the GL texture.</summary>
    [Fact]
    public void Texture2DStrategyField_Resolves()
    {
        EngineReflectionPin.RequireField(typeof(Texture2D), "_strategyTexture2D", NonPublicInstance);
    }

    /// <summary>The WebGLTexture the canvas is uploaded into, without a CPU round trip.</summary>
    [Fact]
    public void TextureStrategyGlTexture_Resolves()
    {
        foreach (var strategy in Texture2DStrategies())
            EngineReflectionPin.RequireField(strategy, "_glTexture", NonPublicInstance);
    }

    /// <summary>The WebGL context the JS side binds against to run the upload.</summary>
    [Fact]
    public void GraphicsContextStrategyGL_Resolves()
    {
        foreach (var strategy in GraphicsContextStrategies())
            EngineReflectionPin.RequireProperty(strategy, "GL", NonPublicInstance);
    }

    /// <summary>
    /// The texture and context handles are passed to JS as their nkast.Wasm <c>Uid</c>, read by name
    /// because the backend stays Blazor-agnostic at compile time and never references that package.
    /// </summary>
    [Fact]
    public void NkastWasmUid_Resolves()
    {
        var glTextures = Texture2DStrategies()
            .Select(s => EngineReflectionPin.RequireField(s, "_glTexture", NonPublicInstance).FieldType);
        var glContexts = GraphicsContextStrategies()
            .Select(s => EngineReflectionPin.RequireProperty(s, "GL", NonPublicInstance).PropertyType);

        foreach (var declared in glTextures.Concat(glContexts))
            foreach (var runtimeType in RuntimeTypes(declared))
                EngineReflectionPin.RequirePropertyOfType(runtimeType, "Uid", PublicInstance, typeof(int));
    }

    /// <summary>
    /// The dirty flags <c>KniStateInvalidation</c> sets so KNI re-applies every piece of state after Skia has
    /// driven its context (SkiaDirectCanvas). They are bools on the strategy base class, which is public; most are
    /// protected internal, two (the buffer flags) public.
    /// </summary>
    [Fact]
    public void ContextStrategyDirtyFlags_Resolve()
    {
        foreach (var name in KniStateInvalidation.DirtyFlagNames)
        {
            var field = EngineReflectionPin.RequireField(typeof(GraphicsContextStrategy), name, PublicInstance | NonPublicInstance);
            Assert.True(field.FieldType == typeof(bool), $"{name} is {field.FieldType.Name}, expected Boolean.");
        }
    }

    /// <summary>
    /// The caches the concrete context keeps of what it last applied, which the dirty flags alone do not
    /// reset. The types are what <c>KniStateInvalidation</c> writes (null, 0) or clears (the set).
    /// </summary>
    [Fact]
    public void ConcreteContextCaches_Resolve()
    {
        foreach (var strategy in GraphicsContextStrategies())
        {
            var program = EngineReflectionPin.RequireField(strategy, KniStateInvalidation.ShaderProgramField, NonPublicInstance);
            Assert.False(program.FieldType.IsValueType, $"{program.Name} is {program.FieldType.Name}; null cannot be assigned to it.");

            var attributes = EngineReflectionPin.RequireField(strategy, KniStateInvalidation.LastVertexAttribsField, NonPublicInstance);
            Assert.True(attributes.FieldType == typeof(int), $"{attributes.Name} is {attributes.FieldType.Name}, expected Int32.");

            var enabled = EngineReflectionPin.RequireField(strategy, KniStateInvalidation.EnabledAttributesField, NonPublicInstance);
            Assert.True(enabled.FieldType == typeof(HashSet<int>), $"{enabled.Name} is {enabled.FieldType.Name}, expected HashSet<Int32>.");
        }
    }

    /// <summary>
    /// <c>KniStateInvalidation</c> keeps the concrete context's members through trimming with a
    /// DynamicDependency that names the type and assembly by string; a rename of either leaves the
    /// attribute pointing at nothing and the trimmer silently dropping the members.
    /// </summary>
    [Fact]
    public void TrimmingRootTarget_Resolves()
    {
        Assert.Equal(KniStateInvalidation.PlatformAssembly, KniPlatform.GetName().Name);
        var type = EngineReflectionPin.RequireType(KniPlatform, KniStateInvalidation.ContextTypeName);
        Assert.Contains(type, GraphicsContextStrategies());
    }

    static IReadOnlyList<Type> Texture2DStrategies() =>
        EngineReflectionPin.RequireImplementations(KniPlatform, typeof(ITexture2DStrategy));

    /// <summary>
    /// The context strategy type is only named by the Strategy property on KNI's platform interface,
    /// so the pin reads it off the interface instead of repeating a name the backend never uses.
    /// </summary>
    static IReadOnlyList<Type> GraphicsContextStrategies()
    {
        var strategyType = EngineReflectionPin
            .RequireProperty(typeof(IPlatformGraphicsContext), "Strategy", PublicInstance).PropertyType;
        return EngineReflectionPin.RequireImplementations(KniPlatform, strategyType);
    }

    /// <summary>
    /// What the backend can be holding when it reads <c>Uid</c>: the declared type when it is
    /// concrete, every nkast.Wasm.Canvas implementation when KNI declares it as an interface.
    /// </summary>
    static IReadOnlyList<Type> RuntimeTypes(Type declared) =>
        declared.IsInterface || declared.IsAbstract
            ? EngineReflectionPin.RequireImplementations(NkastWasmCanvas, declared)
            : new[] { declared };
}
