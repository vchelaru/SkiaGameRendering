#nullable enable
using System;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;

namespace SkiaGameRendering.Unity
{
    /// <summary>Direct3D 11: Skia draws through ANGLE on Unity's own ID3D11Device and immediate context.</summary>
    internal sealed class AngleUnityBackend : SkiaUnityBackend
    {
        readonly AngleSkiaSurfaceFactory _factory;

        // Unity's C# API has no accessor for its ID3D11Device, so the first texture drawn to
        // supplies it: every D3D11 resource can report the device that owns it.
        internal AngleUnityBackend(IntPtr nativeTexture)
        {
            var device = D3D11Com.GetDevice(nativeTexture);
            var context = D3D11Com.GetImmediateContext(device);
            try
            {
                _factory = new AngleSkiaSurfaceFactory();
                _factory.InitializeFromNative(device, context);
            }
            finally
            {
                // Both come back AddRef'd, and Unity keeps them alive for the life of the app.
                D3D11Com.Release(context);
                D3D11Com.Release(device);
            }
        }

        internal override void Draw(SkiaUnityRenderTarget.RenderState target, SKPicture picture)
        {
            _factory.BeginDraw();
            try
            {
                if (target.Surface == null)
                {
                    var textureState = _factory.CreateTextureState(target.NativeTexture);
                    target.TextureState = textureState;
                    (target.Surface, target.BackendRenderTarget) = _factory.CreateSurface(
                        textureState, target.Width, target.Height, SKColorType.Rgba8888);
                }
                else
                {
                    _factory.BindForDrawing((AngleTextureState)target.TextureState!);
                }

                PlayBack(target.Surface, picture, target.Height);
                target.Surface.Flush();
                _factory.UnbindAfterDrawing();
            }
            finally
            {
                _factory.EndDraw();
            }
        }

        internal override void DisposeTarget(SkiaUnityRenderTarget.RenderState target)
        {
            target.Surface?.Dispose();
            target.Surface = null;
            target.BackendRenderTarget?.Dispose();
            target.BackendRenderTarget = null;
            if (target.TextureState is AngleTextureState textureState)
                _factory.DisposeRenderState(textureState);
            target.TextureState = null;
        }

        public override void Dispose() => _factory.Dispose();
    }
}
