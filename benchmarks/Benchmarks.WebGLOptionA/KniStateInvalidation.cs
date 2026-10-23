using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform.Graphics;
using nkast.Wasm.Canvas.WebGL;

namespace Benchmarks.WebGLOptionA;

// Stock-KNI state reset: makes KNI's WebGL
// context forget everything it believes is bound, so its next draw re-applies all state after Skia
// has driven the same context. Reaches KNI's internal members by reflection.
internal static class KniStateInvalidation
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] DirtyFlagNames =
    {
        "_blendStateDirty", "_blendFactorDirty", "_depthStencilStateDirty", "_rasterizerStateDirty",
        "_scissorRectangleDirty", "_vertexBuffersDirty", "_indexBufferDirty", "_vertexShaderDirty", "_pixelShaderDirty",
    };

    private static FieldInfo[]? _dirtyFlags;
    private static FieldInfo? _shaderProgram, _lastVertexAttribs, _enabledAttribs;
    private static PropertyInfo? _gl;

    public static void Invalidate(GraphicsDevice graphicsDevice)
    {
        var context = ((IPlatformGraphicsContext)((IPlatformGraphicsDevice)graphicsDevice).Strategy.CurrentContext).Strategy;
        var concrete = context.GetType();

        _dirtyFlags ??= DirtyFlagNames
            .Select(name => typeof(GraphicsContextStrategy).GetField(name, Instance)
                ?? throw new MissingMemberException($"GraphicsContextStrategy.{name} was not found; KNI's internal layout changed."))
            .ToArray();
        _shaderProgram ??= Field(concrete, "_shaderProgram");
        _lastVertexAttribs ??= Field(concrete, "_lastVertexAttribs");
        _enabledAttribs ??= Field(concrete, "_enabledVertexAttributesSet");
        _gl ??= concrete.GetProperty("GL", Instance)
            ?? throw new MissingMemberException("ConcreteGraphicsContext.GL was not found; KNI's internal layout changed.");

        // Skia may have enabled or disabled vertex attributes behind KNI's back; make GL agree with
        // KNI's belief (nothing enabled) so KNI's next apply enables exactly what it needs.
        var gl = (IWebGLRenderingContext)_gl.GetValue(context)!;
        var enabled = (HashSet<int>)_enabledAttribs.GetValue(context)!;
        foreach (int attribute in enabled)
            gl.DisableVertexAttribArray(attribute);
        enabled.Clear();

        _shaderProgram.SetValue(context, null);
        _lastVertexAttribs.SetValue(context, 0);
        foreach (var flag in _dirtyFlags)
            flag.SetValue(context, true);

        // Public API: re-applies the actual blend/depth/raster states with force = true and resets the
        // vertex-buffer binding cache (what the patch did one field at a time).
        context.PlatformSetup();
        ((IPlatformTextureCollection)graphicsDevice.Textures).Strategy.Dirty();
    }

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, Instance)
        ?? throw new MissingMemberException($"{type.Name}.{name} was not found; KNI's internal layout changed.");
}
