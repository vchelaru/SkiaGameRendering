using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for FNA running on FNA3D's D3D11 driver. Windows only.
    ///
    /// FNA-specific adapter over <see cref="AngleSkiaSurfaceFactory"/> (see that class for how the
    /// ANGLE/D3D11 interop itself works). This class's only job is getting FNA3D's D3D11 device,
    /// context and texture resources out of FNA and handing them to the shared factory.
    ///
    /// REQUIRES FNA3D's D3D11 DRIVER. FNA3D picks its SDL_GPU driver by default on SDL3 builds, and
    /// that driver hands out no native device (see <see cref="Fna3dSysRenderer"/>). Set the
    /// <c>FNA3D_FORCE_DRIVER</c> environment variable to <c>D3D11</c> before constructing the
    /// <c>Game</c>; SDL reads it as a hint when FNA3D selects its driver. <see cref="Initialize"/>
    /// throws with that instruction if any other driver is active.
    ///
    /// MAINTENANCE NOTES:
    /// - <c>GraphicsDevice.GLDevice</c> (the <c>FNA3D_Device*</c>) and <c>Texture.texture</c> (the
    ///   <c>FNA3D_Texture*</c>) are FNA internals reached by reflection; the reflection pin tests in
    ///   <c>tests/Tests.Fna.WindowsDX</c> fail the build if an FNA bump renames either.
    /// - An <c>FNA3D_Texture*</c> from the D3D11 driver is a <c>D3D11Texture</c> struct
    ///   (<c>src/FNA3D_Driver_D3D11.c</c>, "Cast FNA3D_Texture* to this!") whose FIRST field is the
    ///   <c>ID3D11Resource*</c>. FNA3D exposes no API for reading it back, so
    ///   <see cref="CaptureTextureHandle"/> reads that first pointer directly. That is a native
    ///   struct layout, not something a reflection pin can see: the vendored <c>FNA3D.dll</c> under
    ///   <c>external/fnalibs</c> is the one this was verified against, and
    ///   <see cref="CaptureTextureHandle"/> checks the pointer really is an <c>ID3D11Texture2D</c>
    ///   before ANGLE touches it, so a reordered struct is an exception rather than a silent black
    ///   texture. (If a non-pointer field ever moves first, that check itself is what crashes.)
    /// </summary>
    public class SkiaFnaAngleBackend : SkiaBackend
    {
        readonly AngleSkiaSurfaceFactory _factory = new();

        // Reflection handles for FNA internals
        static FieldInfo? _glDeviceField;
        static FieldInfo? _textureField;

        public override GRContext GRContext => _factory.GRContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            // GLDevice is FNA's name for the FNA3D_Device* regardless of driver - a leftover from
            // when FNA's graphics layer was OpenGL-only.
            _glDeviceField ??= typeof(GraphicsDevice).GetField("GLDevice", flags);
            var fna3dDevice = (IntPtr?)_glDeviceField?.GetValue(graphicsDevice) ?? IntPtr.Zero;
            if (fna3dDevice == IntPtr.Zero)
                throw new Exception("Could not extract GLDevice (the FNA3D_Device*) from GraphicsDevice.");

            // texture on the Texture base class holds the FNA3D_Texture*.
            _textureField ??= typeof(Texture).GetField("texture", flags)
                ?? throw new Exception("Could not find the texture field on Texture.");

            var sysRenderer = Fna3dSysRenderer.Get(fna3dDevice);
            if (sysRenderer.RendererType != Fna3dSysRendererType.D3D11)
                throw new InvalidOperationException(
                    $"FNA3D is running its {sysRenderer.RendererType} driver, and only its D3D11 driver exposes " +
                    "the native device SkiaGameRendering.Fna.WindowsDX needs. Set the FNA3D_FORCE_DRIVER " +
                    "environment variable to \"D3D11\" before constructing the Game.");

            _factory.InitializeFromNative(sysRenderer.D3D11Device, sysRenderer.D3D11Context);
        }

        internal override void BeginDraw() => _factory.BeginDraw();

        internal override void EndDraw() => _factory.EndDraw();

        /// <summary>
        /// ANGLE requires textures to have D3D11_BIND_RENDER_TARGET. FNA3D's D3D11 driver only adds
        /// it for render targets (isRenderTarget in D3D11_CreateTexture2D), so a plain Texture2D
        /// would be rejected by eglCreatePbufferFromClientBuffer.
        /// </summary>
        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            return new RenderTarget2D(GraphicsDevice, width, height, false, format, DepthFormat.None);
        }

        internal override object CaptureTextureHandle(Texture2D texture)
        {
            // Unlike MonoGame WindowsDX, FNA allocates the GPU resource in the Texture2D
            // constructor, so no SetData is needed to force it into existence.
            var fna3dTexture = (IntPtr?)_textureField!.GetValue(texture) ?? IntPtr.Zero;
            if (fna3dTexture == IntPtr.Zero)
                throw new Exception($"FNA3D texture is null on Texture2D ({texture.Width}x{texture.Height}).");

            // D3D11Texture.handle - see the MAINTENANCE NOTES on this class.
            var d3dPtr = Marshal.ReadIntPtr(fna3dTexture);
            if (d3dPtr == IntPtr.Zero)
                throw new Exception($"D3D11 resource is null on Texture2D ({texture.Width}x{texture.Height}).");

            IntPtr texture2d;
            try
            {
                texture2d = D3D11Com.QueryInterface(d3dPtr, D3D11Com.IID_ID3D11Texture2D);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    "The first field of FNA3D's D3D11 texture struct is not an ID3D11Texture2D. The vendored " +
                    "FNA3D.dll's D3D11Texture layout has changed; see SkiaFnaAngleBackend's MAINTENANCE NOTES.",
                    exception);
            }
            D3D11Com.Release(texture2d);

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
