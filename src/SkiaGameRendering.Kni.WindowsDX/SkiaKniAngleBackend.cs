using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform.Graphics;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using System.Reflection;

namespace SkiaGameRendering.Kni.WindowsDX
{
    /// <summary>
    /// SkiaBackend for KNI's WinForms DX11 (D3D11) platform. Windows only.
    ///
    /// KNI-specific adapter over <see cref="AngleSkiaSurfaceFactory"/> (see that class for how the
    /// ANGLE/D3D11 interop itself works). KNI exposes a public Strategy-pattern bridge down to its
    /// platform internals (<see cref="IPlatformGraphicsDevice"/>, <see cref="IPlatformGraphicsContext"/>,
    /// <see cref="IPlatformTexture"/>), so only the last hop into each concrete strategy's SharpDX
    /// field needs reflection.
    ///
    /// MAINTENANCE NOTES:
    /// - "D3DDevice", "D3dContext" and "GetTexture" are internal members of KNI's
    ///   ConcreteGraphicsDevice/ConcreteGraphicsContext/ConcreteTexture. They may change between
    ///   KNI versions.
    /// </summary>
    public class SkiaKniAngleBackend : SkiaBackend
    {
        readonly AngleSkiaSurfaceFactory _factory = new();

        static PropertyInfo? _d3dDeviceProperty;
        static PropertyInfo? _d3dContextProperty;
        static MethodInfo? _getTextureMethod;

        public override GRContext GRContext => _factory.GRContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            var deviceStrategy = ((IPlatformGraphicsDevice)graphicsDevice).Strategy;
            _d3dDeviceProperty ??= deviceStrategy.GetType().GetProperty("D3DDevice", flags)
                ?? throw new Exception("Could not find internal D3DDevice property on KNI's ConcreteGraphicsDevice.");
            var d3dDevice = _d3dDeviceProperty.GetValue(deviceStrategy)
                ?? throw new Exception("D3DDevice was null on KNI's ConcreteGraphicsDevice.");

            var contextStrategy = ((IPlatformGraphicsContext)deviceStrategy.CurrentContext).Strategy;
            _d3dContextProperty ??= contextStrategy.GetType().GetProperty("D3dContext", flags)
                ?? throw new Exception("Could not find internal D3dContext property on KNI's ConcreteGraphicsContext.");
            var d3dContext = _d3dContextProperty.GetValue(contextStrategy)
                ?? throw new Exception("D3dContext was null on KNI's ConcreteGraphicsContext.");

            _factory.Initialize(d3dDevice, d3dContext);
        }

        internal override void BeginDraw() => _factory.BeginDraw();

        internal override void EndDraw() => _factory.EndDraw();

        IntPtr GetD3DTexturePtr(Texture2D texture)
        {
            var textureStrategy = ((IPlatformTexture)texture).GetTextureStrategy<ITexture2DStrategy>();
            _getTextureMethod ??= textureStrategy.GetType().GetMethod("GetTexture", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new Exception("Could not find internal GetTexture() method on KNI's ConcreteTexture.");

            var sharpDxResource = _getTextureMethod.Invoke(textureStrategy, null)
                ?? throw new Exception($"D3D11 resource is null on Texture2D ({texture.Width}x{texture.Height}).");
            return AngleSkiaSurfaceFactory.GetNativePointer(sharpDxResource);
        }

        /// <summary>
        /// ANGLE requires textures to have D3D11_BIND_RENDER_TARGET. RenderTarget2D sets it (plain
        /// Texture2D doesn't), and unlike MonoGame WindowsDX, KNI's RenderTarget2D allocates its
        /// D3D11 resource eagerly - no SetData workaround needed here.
        /// </summary>
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format, DepthFormat.None);
        }

        internal override object CaptureTextureHandle(Texture2D texture)
        {
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
