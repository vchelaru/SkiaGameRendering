using System.Reflection;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using Stride.Graphics;

namespace SkiaGameRendering.Stride
{
    /// <summary>
    /// Stride-specific adapter over <see cref="AngleSkiaSurfaceFactory"/> (see that class for how the
    /// ANGLE/D3D11 interop itself works). This class's only job is pulling Stride's D3D11
    /// device/context/texture pointers and handing them to the shared factory.
    ///
    /// MAINTENANCE NOTES:
    /// - Targets Stride 4.4.0-beta5+, whose D3D11 backend is Silk.NET rather than SharpDX (see
    ///   eng/Versions.props). <see cref="GraphicsDevice.NativeDevice"/> is a public
    ///   <c>ComPtr&lt;ID3D11Device&gt;</c> on this version, unlike MonoGame/KNI WindowsDX, so no
    ///   reflection is needed for the device.
    /// - Stride's immediate-context accessor is not public (unlike the device), so
    ///   <see cref="Initialize"/> asks the device for it directly over COM
    ///   (<see cref="D3D11Com.GetImmediateContext"/>) instead of reflecting a private field.
    /// - <see cref="GetD3DResourcePtr"/> reflects the one Stride internal this adapter needs:
    ///   <c>GraphicsResourceBase.nativeResource</c> (a private <c>ID3D11Resource*</c> field). The
    ///   public <c>NativeResource</c> property wrapping it is <c>protected internal</c> - visible to
    ///   a subclass of <see cref="GraphicsResourceBase"/>, not to an external caller like this one.
    ///   A Stride bump that renames this field is pinned by
    ///   <c>tests/Tests.Stride/StrideReflectionTests.cs</c>.
    /// </summary>
    internal sealed class SkiaStrideContext : IDisposable
    {
        readonly AngleSkiaSurfaceFactory _factory = new();

        static FieldInfo? _nativeResourceField;

        internal GRContext GRContext => _factory.GRContext;

        internal void Initialize(GraphicsDevice graphicsDevice)
        {
            IntPtr devicePtr;
            unsafe { devicePtr = (IntPtr)graphicsDevice.NativeDevice.Handle; }
            if (devicePtr == IntPtr.Zero)
                throw new Exception("Stride GraphicsDevice.NativeDevice is null.");

            // GetImmediateContext AddRefs the context it returns; InitializeFromNative's own
            // QueryInterface (see AngleSkiaSurfaceFactory.InitD3D11StateSwap) takes its own separate
            // reference, so this one is released as soon as it's been handed off.
            var contextPtr = D3D11Com.GetImmediateContext(devicePtr);
            try
            {
                _factory.InitializeFromNative(devicePtr, contextPtr);
            }
            finally
            {
                D3D11Com.Release(contextPtr);
            }
        }

        internal void BeginDraw() => _factory.BeginDraw();

        internal void EndDraw() => _factory.EndDraw();

        internal AngleTextureState CreateTextureState(Texture texture) =>
            _factory.CreateTextureState(GetD3DResourcePtr(texture));

        internal (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            AngleTextureState state, int width, int height, SKColorType colorType, SKColorSpace? colorSpace = null) =>
            _factory.CreateSurface(state, width, height, colorType, colorSpace);

        internal void BindForDrawing(AngleTextureState state) => _factory.BindForDrawing(state);

        internal void UnbindAfterDrawing() => _factory.UnbindAfterDrawing();

        internal void DisposeRenderState(AngleTextureState state) => _factory.DisposeRenderState(state);

        static IntPtr GetD3DResourcePtr(Texture texture)
        {
            _nativeResourceField ??= typeof(GraphicsResourceBase).GetField(
                "nativeResource", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new Exception("Could not find Stride.Graphics.GraphicsResourceBase.nativeResource.");

            var boxedPointer = _nativeResourceField.GetValue(texture)
                ?? throw new Exception($"D3D11 resource is null on Texture ({texture.Width}x{texture.Height}).");

            // Reflection boxes an unsafe pointer field's value as System.Reflection.Pointer, not a
            // raw IntPtr - Pointer.Unbox is required to get the address back out.
            unsafe { return (IntPtr)Pointer.Unbox(boxedPointer); }
        }

        public void Dispose() => _factory.Dispose();
    }
}
