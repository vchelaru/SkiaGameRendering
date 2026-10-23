using System.Runtime.InteropServices;
using SkiaGameRendering.Core.ANGLE;
using SkiaSharp;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// <c>AngleSkiaSurfaceFactory.BeginDirectDraw</c> on WARP: drawing into whatever render target the
/// "engine" (this test) has bound on the immediate context. The engine tests cover RGBA back buffers and
/// targets; this covers the formats no engine here hands out, the refusals, and that a pass leaves the
/// binding and the texture's reference count as it found them.
/// </summary>
public sealed unsafe class AngleDirectDrawTests
{
    const int Size = 8;
    const int DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29;
    const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    static readonly SKColor SkiaColor = new(210, 50, 90);

    [Theory]
    [InlineData(D3D11RawResources.DXGI_FORMAT_R8G8B8A8_UNORM)]
    [InlineData(DXGI_FORMAT_B8G8R8A8_UNORM)]
    public void DrawsIntoBoundTarget_WithoutClearing_AndRestoresBinding(int format)
    {
        using var setup = new BoundTarget(format);

        var referencesBefore = RefCount(setup.Texture);
        var canvas = setup.Factory.BeginDirectDraw(Size, Size, out var pass);
        using (var paint = new SKPaint { Color = SkiaColor })
            canvas.DrawRect(0, 0, Size / 2, Size, paint);
        setup.Factory.EndDirectDraw(pass);

        Assert.Equal(referencesBefore, RefCount(setup.Texture));

        var views = new IntPtr[D3D11Com.SimultaneousRenderTargetCount];
        D3D11Com.OMGetRenderTargets(setup.Context, views);
        try
        {
            Assert.Equal(setup.View, views[0]);
        }
        finally
        {
            foreach (var view in views.Where(view => view != IntPtr.Zero))
                D3D11Com.Release(view);
        }

        var pixels = setup.ReadBack();
        var bgra = format == DXGI_FORMAT_B8G8R8A8_UNORM;
        byte[] Expected(SKColor c) => bgra ? [c.Blue, c.Green, c.Red, c.Alpha] : [c.Red, c.Green, c.Blue, c.Alpha];
        Assert.Equal(Expected(SkiaColor), pixels[0..4]);
        // The right half keeps the engine's clear color: a direct pass does not clear.
        Assert.Equal(Expected(BoundTarget.ClearColor), pixels[((Size - 1) * 4)..(Size * 4)]);
    }

    [Fact]
    public void SrgbTarget_IsNotSupported()
    {
        using var setup = new BoundTarget(DXGI_FORMAT_R8G8B8A8_UNORM_SRGB);
        Assert.Throws<NotSupportedException>(() => setup.Factory.BeginDirectDraw(Size, Size, out _));
    }

    [Fact]
    public void SizeTheEngineDisagreesWith_Throws()
    {
        using var setup = new BoundTarget(D3D11RawResources.DXGI_FORMAT_R8G8B8A8_UNORM);
        Assert.Throws<InvalidOperationException>(() => setup.Factory.BeginDirectDraw(Size * 2, Size, out _));
    }

    [Fact]
    public void NothingBound_Throws()
    {
        using var setup = new BoundTarget(D3D11RawResources.DXGI_FORMAT_R8G8B8A8_UNORM);
        D3D11Com.OMUnbindRenderTargets(setup.Context);
        Assert.Throws<InvalidOperationException>(() => setup.Factory.BeginDirectDraw(Size, Size, out _));
    }

    /// <summary>The reference count, read by an AddRef (IUnknown slot 1) and the Release that returns it.</summary>
    static uint RefCount(IntPtr unknown)
    {
        var addRef = (delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(void***)unknown)[1];
        addRef(unknown);
        return D3D11Com.Release(unknown);
    }

    /// <summary>A WARP device with an 8x8 render target cleared to <see cref="ClearColor"/> and bound, as an engine would leave it.</summary>
    sealed class BoundTarget : IDisposable
    {
        internal static readonly SKColor ClearColor = new(20, 30, 200);

        readonly IntPtr _device;
        readonly D3D11RawResources.Texture2DDesc _desc;

        internal IntPtr Context { get; }
        internal IntPtr Texture { get; }
        internal IntPtr View { get; }
        internal AngleSkiaSurfaceFactory Factory { get; } = new();

        internal BoundTarget(int format)
        {
            (_device, var context) = WarpDevice.Create();
            Context = context;
            Factory.InitializeFromNative(_device, Context);

            _desc = new D3D11RawResources.Texture2DDesc
            {
                Width = Size,
                Height = Size,
                MipLevels = 1,
                ArraySize = 1,
                Format = format,
                SampleCount = 1,
                Usage = D3D11RawResources.D3D11_USAGE_DEFAULT,
                BindFlags = D3D11RawResources.D3D11_BIND_RENDER_TARGET | D3D11RawResources.D3D11_BIND_SHADER_RESOURCE,
            };
            Texture = D3D11RawResources.CreateTexture2D(_device, _desc);
            View = D3D11RawResources.CreateRenderTargetView(_device, Texture);
            D3D11RawResources.ClearRenderTargetView(Context, View,
                ClearColor.Red / 255f, ClearColor.Green / 255f, ClearColor.Blue / 255f, 1f);
            D3D11RawResources.SetRenderTarget(Context, View);
        }

        /// <summary>The first row, as the texture's own byte order.</summary>
        internal byte[] ReadBack()
        {
            var stagingDesc = _desc;
            stagingDesc.Usage = D3D11RawResources.D3D11_USAGE_STAGING;
            stagingDesc.BindFlags = 0;
            stagingDesc.CPUAccessFlags = D3D11RawResources.D3D11_CPU_ACCESS_READ;
            var staging = D3D11RawResources.CreateTexture2D(_device, stagingDesc);
            try
            {
                D3D11RawResources.CopyResource(Context, staging, Texture);
                var row = new byte[Size * 4];
                D3D11RawResources.MapReadAndCopy(Context, staging, (data, _) => Marshal.Copy(data, row, 0, row.Length));
                return row;
            }
            finally
            {
                D3D11Com.Release(staging);
            }
        }

        public void Dispose()
        {
            Factory.Dispose();
            D3D11Com.OMUnbindRenderTargets(Context);
            D3D11Com.Release(View);
            D3D11Com.Release(Texture);
            D3D11Com.Release(Context);
            D3D11Com.Release(_device);
        }
    }
}
