using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform.Graphics;

namespace SkiaGameRendering
{
    // Makes KNI's Android GL context forget everything it believes is applied, after Skia drew a back
    // buffer pass on that same context. KNI caches the GL state it set and skips calls it thinks are
    // redundant, so without this it draws with Skia's leftover program, buffers, blend and depth state.
    //
    // Same approach as the KNI WebGL backend's KniStateInvalidation, against the Android context class
    // (ConcreteGraphicsContextGL, the base of the runtime type, where the cache fields live). KNI's own
    // Cardboard_ResetContext is close but clears the texture collection, which would drop textures the
    // game set. Every member is resolved on the first pass and throws MissingMemberException naming it,
    // so a KNI bump that renames one fails CI's Android smoke test at once.
    internal sealed class KniAndroidStateInvalidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] DirtyFlagNames =
        {
            "_blendStateDirty", "_blendFactorDirty", "_depthStencilStateDirty", "_rasterizerStateDirty",
            "_scissorRectangleDirty", "_vertexBuffersDirty", "_indexBufferDirty", "_vertexShaderDirty", "_pixelShaderDirty",
        };

        private static readonly object True = true;
        private static readonly object Zero = 0;
        private static readonly object NoClearColor = new Vector4(float.MaxValue);
        private static readonly object NoClearDepth = float.MaxValue;
        private static readonly object NoClearStencil = int.MaxValue;

        private FieldInfo[]? _dirtyFlags;
        private FieldInfo _shaderProgram = null!, _lastVertexAttribs = null!, _enabledAttributes = null!;
        private FieldInfo _lastClearColor = null!, _lastClearDepth = null!, _lastClearStencil = null!;

        internal void Invalidate(GraphicsDevice graphicsDevice)
        {
            var context = ((IPlatformGraphicsContext)((IPlatformGraphicsDevice)graphicsDevice).Strategy.CurrentContext).Strategy;

            if (_dirtyFlags == null)
            {
                var type = context.GetType();
                _shaderProgram = Field(type, "_shaderProgram");
                _lastVertexAttribs = Field(type, "_lastVertexAttribs");
                _enabledAttributes = Field(type, "_enabledVertexAttributesSet");
                _lastClearColor = Field(type, "_lastClearColor");
                _lastClearDepth = Field(type, "_lastClearDepth");
                _lastClearStencil = Field(type, "_lastClearStencil");
                _dirtyFlags = DirtyFlagNames.Select(name => Field(typeof(GraphicsContextStrategy), name)).ToArray();
            }

            // GlesEngineStateReset already disabled every vertex attribute; this is KNI's record of that.
            ((HashSet<int>)_enabledAttributes.GetValue(context)!).Clear();
            _shaderProgram.SetValue(context, null);
            _lastVertexAttribs.SetValue(context, Zero);
            _lastClearColor.SetValue(context, NoClearColor);
            _lastClearDepth.SetValue(context, NoClearDepth);
            _lastClearStencil.SetValue(context, NoClearStencil);
            foreach (var flag in _dirtyFlags)
                flag.SetValue(context, True);

            // Public: re-applies the actual blend, depth and rasterizer states with force = true and resets
            // the vertex-buffer binding cache.
            context.PlatformSetup();
            ((IPlatformTextureCollection)graphicsDevice.Textures).Strategy.Dirty();
            ((IPlatformTextureCollection)graphicsDevice.VertexTextures).Strategy.Dirty();

            // The concrete setter applies the viewport to GL; Skia left its own.
            context.Viewport = context.Viewport;
        }

        // Private fields of a base class are not visible through the derived type, so walk up.
        private static FieldInfo Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingMemberException($"{type.Name}.{name} was not found; KNI's internal layout changed.");
        }
    }
}
