using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SkiaGameRendering
{
    // Makes MonoGame Android's GraphicsDevice forget everything it believes is applied, after Skia drew a
    // back buffer pass on MonoGame's own GL context. MonoGame caches the GL state it set and skips calls
    // it thinks are redundant, so without this it draws with Skia's leftover program, buffers, blend and
    // depth state.
    //
    // MonoGame has no public API for it, so this sets the same fields and makes the same forced applies
    // GraphicsDevice.PlatformInitialize does. Every member is resolved on the first pass and throws
    // MissingMemberException naming it, so a MonoGame bump that renames one fails CI's Android smoke test.
    internal sealed class MonoGameStateInvalidation
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] DirtyFlagNames =
        {
            "_blendStateDirty", "_blendFactorDirty", "_depthStencilStateDirty", "_rasterizerStateDirty",
            "_scissorRectangleDirty", "_vertexBuffersDirty", "_indexBufferDirty", "_vertexShaderDirty", "_pixelShaderDirty",
        };

        private static readonly object True = true;
        private static readonly object NoClearColor = new Vector4(float.MaxValue);
        private static readonly object NoClearDepth = float.MaxValue;
        private static readonly object NoClearStencil = int.MaxValue;

        private FieldInfo[]? _dirtyFlags;
        private FieldInfo _shaderProgram = null!, _attribsDirty = null!, _enabledAttributes = null!;
        private FieldInfo _lastClearColor = null!, _lastClearDepth = null!, _lastClearStencil = null!;
        private FieldInfo _actualDepthStencil = null!, _actualRasterizer = null!;
        private Action<GraphicsDevice, bool> _applyBlend = null!;
        private Action<DepthStencilState, GraphicsDevice, bool> _applyDepthStencil = null!;
        private Action<RasterizerState, GraphicsDevice, bool> _applyRasterizer = null!;
        private Action<TextureCollection> _dirtyTextures = null!;

        internal void Invalidate(GraphicsDevice device)
        {
            if (_dirtyFlags == null)
                Resolve();

            // GlesEngineStateReset already disabled every vertex attribute; this is MonoGame's record of that.
            ((List<int>)_enabledAttributes.GetValue(null)!).Clear();
            _attribsDirty.SetValue(null, True);
            _shaderProgram.SetValue(device, null);
            _lastClearColor.SetValue(device, NoClearColor);
            _lastClearDepth.SetValue(device, NoClearDepth);
            _lastClearStencil.SetValue(device, NoClearStencil);
            foreach (var flag in _dirtyFlags!)
                flag.SetValue(device, True);

            _applyBlend(device, true);
            _applyDepthStencil((DepthStencilState)_actualDepthStencil.GetValue(device)!, device, true);
            _applyRasterizer((RasterizerState)_actualRasterizer.GetValue(device)!, device, true);
            _dirtyTextures(device.Textures);
            _dirtyTextures(device.VertexTextures);

            // The setter applies the viewport to GL; Skia left its own.
            device.Viewport = device.Viewport;
        }

        private void Resolve()
        {
            var deviceType = typeof(GraphicsDevice);
            _shaderProgram = Field(deviceType, "_shaderProgram", Instance);
            _attribsDirty = Field(deviceType, "_attribsDirty", Static);
            _enabledAttributes = Field(deviceType, "_enabledVertexAttributes", Static);
            _lastClearColor = Field(deviceType, "_lastClearColor", Instance);
            _lastClearDepth = Field(deviceType, "_lastClearDepth", Instance);
            _lastClearStencil = Field(deviceType, "_lastClearStencil", Instance);
            _actualDepthStencil = Field(deviceType, "_actualDepthStencilState", Instance);
            _actualRasterizer = Field(deviceType, "_actualRasterizerState", Instance);

            _applyBlend = Method<Action<GraphicsDevice, bool>>(deviceType, "PlatformApplyBlend", typeof(bool));
            _applyDepthStencil = Method<Action<DepthStencilState, GraphicsDevice, bool>>(
                typeof(DepthStencilState), "PlatformApplyState", typeof(GraphicsDevice), typeof(bool));
            _applyRasterizer = Method<Action<RasterizerState, GraphicsDevice, bool>>(
                typeof(RasterizerState), "PlatformApplyState", typeof(GraphicsDevice), typeof(bool));
            _dirtyTextures = Method<Action<TextureCollection>>(typeof(TextureCollection), "Dirty");

            _dirtyFlags = DirtyFlagNames.Select(name => Field(deviceType, name, Instance)).ToArray();
        }

        private static FieldInfo Field(Type type, string name, BindingFlags flags) =>
            type.GetField(name, flags)
            ?? throw new MissingMemberException($"{type.Name}.{name} was not found; MonoGame's internal layout changed.");

        // An open-instance delegate, so a pass calls the method without reflection's argument arrays.
        private static T Method<T>(Type type, string name, params Type[] parameters) where T : Delegate =>
            (T)Delegate.CreateDelegate(typeof(T),
                type.GetMethod(name, Instance, null, parameters, null)
                ?? throw new MissingMemberException($"{type.Name}.{name} was not found; MonoGame's internal layout changed."));
    }
}
