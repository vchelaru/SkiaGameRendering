using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform.Graphics;

namespace SkiaGameRendering;

// Makes KNI's WebGL context forget everything it believes is bound, so its next draw re-applies all state
// after Skia has driven the same context (SkiaDirectCanvas). KNI keeps a cache of the GL state it has
// applied and skips calls it thinks are redundant; Skia leaves its own program, attributes, blend and depth
// state behind, so without this KNI draws with Skia's leftovers.
//
// KNI has no public API for it, so this reaches the members a ConcreteGraphicsContext (BlazorGL) keeps its
// cache in. Like KniWebGlInternals, everything is resolved lazily and throws MissingMemberException naming
// the member, and tests/Tests.Kni.WebGL.Reflection pins each one. The DynamicDependency attributes keep
// the members through IL trimming and WASM AOT; the context type is only known at run time, so the
// trimmer cannot see these lookups.
internal static class KniStateInvalidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal const string ContextTypeName = "Microsoft.Xna.Platform.Graphics.ConcreteGraphicsContext";
    internal const string PlatformAssembly = "Kni.Platform";

    internal static readonly string[] DirtyFlagNames =
    {
        "_blendStateDirty", "_blendFactorDirty", "_depthStencilStateDirty", "_rasterizerStateDirty",
        "_scissorRectangleDirty", "_vertexBuffersDirty", "_indexBufferDirty", "_vertexShaderDirty", "_pixelShaderDirty",
    };

    internal const string ShaderProgramField = "_shaderProgram";
    internal const string LastVertexAttribsField = "_lastVertexAttribs";
    internal const string EnabledAttributesField = "_enabledVertexAttributesSet";

    private static FieldInfo[]? _dirtyFlags;
    private static FieldInfo? _shaderProgram, _lastVertexAttribs, _enabledAttributes;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields, typeof(GraphicsContextStrategy))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields | DynamicallyAccessedMemberTypes.NonPublicProperties,
        ContextTypeName, PlatformAssembly)]
    internal static void Invalidate(GraphicsDevice graphicsDevice)
    {
        var context = ((IPlatformGraphicsContext)((IPlatformGraphicsDevice)graphicsDevice).Strategy.CurrentContext).Strategy;
        var concrete = context.GetType();

        _dirtyFlags ??= DirtyFlagNames
            .Select(name => typeof(GraphicsContextStrategy).GetField(name, Instance)
                ?? throw new MissingMemberException($"GraphicsContextStrategy.{name} was not found; KNI's internal layout changed."))
            .ToArray();
        _shaderProgram ??= Field(concrete, ShaderProgramField);
        _lastVertexAttribs ??= Field(concrete, LastVertexAttribsField);
        _enabledAttributes ??= Field(concrete, EnabledAttributesField);

        // The JS side already disabled every vertex attribute on the GL context; this is KNI's record of it.
        ((HashSet<int>)_enabledAttributes.GetValue(context)!).Clear();
        _shaderProgram.SetValue(context, null);
        _lastVertexAttribs.SetValue(context, 0);
        foreach (var flag in _dirtyFlags)
            flag.SetValue(context, true);

        // Public, and what makes the dirty flags enough: re-applies the actual blend, depth and rasterizer
        // states with force = true and resets the vertex-buffer binding cache.
        context.PlatformSetup();
        ((IPlatformTextureCollection)graphicsDevice.Textures).Strategy.Dirty();
        ((IPlatformTextureCollection)graphicsDevice.VertexTextures).Strategy.Dirty();

        // The concrete setter applies the viewport to GL; Skia left its own.
        context.Viewport = context.Viewport;
    }

    // The concrete context type is only known at run time, so the trimmer cannot see this lookup; the
    // DynamicDependency attributes on Invalidate keep every member it reaches.
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Kept by the DynamicDependency attributes on Invalidate.")]
    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, Instance)
        ?? throw new MissingMemberException($"{type.Name}.{name} was not found; KNI's internal layout changed.");
}
