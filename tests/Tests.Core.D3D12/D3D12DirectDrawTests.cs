using SkiaGameRendering.Core.D3D12;
using SkiaSharp;
using Xunit;

namespace Tests.CoreD3D12;

/// <summary>
/// <see cref="D3D12SkiaSurfaceFactory.DescribeDirectTarget"/> and the begin/end pair on a real WARP device:
/// which engine targets Skia may draw into, and that a refused one leaves the factory usable. The pixels a
/// pass produces are checked through Stride in <c>Tests.Stride.D3D12</c>, where an engine owns the target.
/// </summary>
public sealed class D3D12DirectDrawTests
{
    const uint Rgba8Unorm = D3D12Constants.FormatR8G8B8A8Unorm;
    const uint Bgra8Unorm = D3D12Constants.FormatB8G8R8A8Unorm;

    [Theory]
    [InlineData(Rgba8Unorm, SKColorType.Rgba8888)]
    [InlineData(Bgra8Unorm, SKColorType.Bgra8888)]
    public void SupportedFormat_IsDescribedWithItsSkiaColorType(uint format, SKColorType expected)
    {
        using var d3d12 = new D3D12TestDevice();
        var resource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, 8, 4, format);
        try
        {
            var target = D3D12SkiaSurfaceFactory.DescribeDirectTarget(resource, 8, 4);

            Assert.Equal(expected, target.ColorType);
        }
        finally
        {
            D3D12SkiaSurfaceFactory.ReleaseResource(resource);
        }
    }

    /// <summary>Typeless has no render target view of its own, sRGB would be encoded, HDR is not 8-bit.</summary>
    [Theory]
    [InlineData(D3D12Constants.FormatR8G8B8A8Typeless)]
    [InlineData(D3D12Constants.FormatR8G8B8A8UnormSrgb)]
    [InlineData(D3D12Constants.FormatB8G8R8A8Typeless)]
    [InlineData(D3D12Constants.FormatB8G8R8A8UnormSrgb)]
    [InlineData(D3D12Constants.FormatR10G10B10A2Unorm)]
    [InlineData(D3D12Constants.FormatR16G16B16A16Unorm)]
    public void UnsupportedFormat_ThrowsNotSupported(uint format)
    {
        using var d3d12 = new D3D12TestDevice();
        var resource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, 8, 4, format);
        try
        {
            Assert.Throws<NotSupportedException>(() => D3D12SkiaSurfaceFactory.DescribeDirectTarget(resource, 8, 4));
        }
        finally
        {
            D3D12SkiaSurfaceFactory.ReleaseResource(resource);
        }
    }

    [Fact]
    public void SizeTheEngineDoesNotAgreeWith_Throws()
    {
        using var d3d12 = new D3D12TestDevice();
        var resource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, 8, 4, Rgba8Unorm);
        try
        {
            Assert.Throws<InvalidOperationException>(() => D3D12SkiaSurfaceFactory.DescribeDirectTarget(resource, 4, 8));
            Assert.Throws<InvalidOperationException>(() => D3D12SkiaSurfaceFactory.DescribeDirectTarget(resource, 8, 8));
        }
        finally
        {
            D3D12SkiaSurfaceFactory.ReleaseResource(resource);
        }
    }

    [Fact]
    public void NullResource_Throws() =>
        Assert.Throws<ArgumentException>(() => D3D12SkiaSurfaceFactory.DescribeDirectTarget(IntPtr.Zero, 8, 4));

    /// <summary>
    /// A pass takes the queue lock in Begin and releases it in End, and a Begin that throws releases it too:
    /// the lock is a hook the host's own monitor hangs off, and a leaked hold would deadlock its next submit.
    /// </summary>
    [Fact]
    public void Pass_AcquiresAndReleasesTheQueueLock_AndDrawsTwice()
    {
        using var d3d12 = new D3D12TestDevice();
        var resource = D3D12SkiaSurfaceFactory.CreateRenderTargetResource(d3d12.Device, 8, 4, Rgba8Unorm);
        var held = 0;
        try
        {
            using var factory = new D3D12SkiaSurfaceFactory();
            factory.InitializeFromNative(d3d12.Adapter, d3d12.Device, d3d12.Queue, () =>
            {
                held++;
                return new Release(() => held--);
            });
            var target = D3D12SkiaSurfaceFactory.DescribeDirectTarget(resource, 8, 4);

            for (var i = 0; i < 2; i++)
            {
                var canvas = factory.BeginDirectDraw(target, out var pass);
                Assert.Equal(1, held);
                canvas.Clear(SKColors.Red);
                factory.EndDirectDraw(pass, synchronous: true);
                Assert.Equal(0, held);
            }
        }
        finally
        {
            D3D12SkiaSurfaceFactory.ReleaseResource(resource);
        }
    }

    sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
