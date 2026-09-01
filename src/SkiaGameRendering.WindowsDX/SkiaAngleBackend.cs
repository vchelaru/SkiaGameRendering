using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using System.Reflection;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for MonoGame's WindowsDX (D3D11) platform. Windows only.
    ///
    /// MonoGame-specific adapter over <see cref="AngleSkiaSurfaceFactory"/> (see that class for how
    /// the ANGLE/D3D11 interop itself works). This class's only job is extracting MonoGame's
    /// internal D3D11 device/context/texture objects via reflection and handing them to the
    /// shared factory.
    ///
    /// MAINTENANCE NOTES:
    /// - Field names (_d3dDevice, _d3dContext, _texture) are MonoGame internals
    ///   accessed via reflection. They may change between MG versions.
    /// </summary>
    public class SkiaAngleBackend : SkiaBackend
    {
        readonly AngleSkiaSurfaceFactory _factory = new();

        // Reflection handles for MonoGame internals
        static FieldInfo? _d3dDeviceField;
        static FieldInfo? _d3dContextField;
        static FieldInfo? _textureField;

        public override GRContext GRContext => _factory.GRContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            // Extract MonoGame's internal D3D11 device and context via reflection.
            // These are SharpDX wrapper objects around the native COM interfaces.
            _d3dDeviceField ??= typeof(GraphicsDevice).GetField("_d3dDevice", flags);

            var d3dDevice = _d3dDeviceField?.GetValue(graphicsDevice)
                ?? throw new Exception("Could not extract _d3dDevice from GraphicsDevice.");

            // _texture on the Texture base class holds the SharpDX.Direct3D11.Resource
            _textureField ??= typeof(Texture).GetField("_texture", flags);

            _d3dContextField ??= typeof(GraphicsDevice).GetField("_d3dContext", flags);
            var d3dContext = _d3dContextField!.GetValue(graphicsDevice)
                ?? throw new Exception("Could not extract _d3dContext from GraphicsDevice.");

            _factory.Initialize(d3dDevice, d3dContext);
        }

        internal override void BeginDraw() => _factory.BeginDraw();

        internal override void EndDraw() => _factory.EndDraw();

        IntPtr GetD3DTexturePtr(Texture2D texture)
        {
            var sharpDxResource = _textureField!.GetValue(texture);
            if (sharpDxResource == null)
                throw new Exception(
                    $"D3D11 resource is null on Texture2D ({texture.Width}x{texture.Height}).");
            return AngleSkiaSurfaceFactory.GetNativePointer(sharpDxResource);
        }

        /// <summary>
        /// ANGLE requires textures to have D3D11_BIND_RENDER_TARGET. MonoGame's
        /// Texture2D only sets BIND_SHADER_RESOURCE. RenderTarget2D sets both.
        /// </summary>
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format, DepthFormat.None);
        }

        internal override object CaptureTextureHandle(Texture2D texture)
        {
            // WORKAROUND: MonoGame WindowsDX lazily allocates the D3D11 resource.
            // After new Texture2D(), the internal _texture field is null until MG
            // actually needs the GPU resource. SetData forces creation.
            // TODO: Find a cheaper way to force D3D11 resource allocation.
            texture.SetData(new byte[texture.Width * texture.Height * 4]);

            var d3dPtr = GetD3DTexturePtr(texture);
            return _factory.CreateTextureState(d3dPtr);
        }

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var state = (AngleTextureState)textureHandle;
            var result = _factory.CreateSurface(state, width, height, colorType);
            renderState = state;
            return result;
        }

        internal override void BindForDrawing(object renderState) => _factory.BindForDrawing((AngleTextureState)renderState);

        internal override void UnbindAfterDrawing() => _factory.UnbindAfterDrawing();

        internal override void DisposeRenderState(object renderState) => _factory.DisposeRenderState((AngleTextureState)renderState);

        public override void Dispose() => _factory.Dispose();
    }
}
