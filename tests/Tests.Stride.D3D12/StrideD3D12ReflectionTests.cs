using System.Reflection;
using SkiaGameRendering.Core.D3D12;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Stride.Graphics;
using BarrierLayout = Stride.Graphics.BarrierLayout;
using Tests.Shared;
using Xunit;

namespace Tests.StrideD3D12;

/// <summary>
/// Pins every Stride D3D12 member <c>SkiaStrideD3D12Context</c> reaches by string
/// (src/SkiaGameRendering.Stride.D3D12/SkiaStrideD3D12Context.cs), plus the enum values it relies
/// on matching. <c>Tests.Stride.D3D12.csproj</c> sets <c>StrideGraphicsApi=Direct3D12</c>, so these
/// bind to the Direct3D12 build of Stride.Graphics, the one the adapter reflects into at runtime.
/// </summary>
public sealed class StrideD3D12ReflectionTests
{
    const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void NativeDeviceProperty_ResolvesToID3D12Device() =>
        EngineReflectionPin.RequirePropertyOfType(
            typeof(GraphicsDevice), "NativeDevice", PublicInstance, typeof(ComPtr<ID3D12Device>));

    [Fact]
    public void NativeCommandQueueProperty_ResolvesToID3D12CommandQueue() =>
        EngineReflectionPin.RequirePropertyOfType(
            typeof(GraphicsDevice), "NativeCommandQueue", NonPublicInstance, typeof(ComPtr<ID3D12CommandQueue>));

    [Fact]
    public void QueueLockField_ResolvesToObject()
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsDevice), "QueueLock", NonPublicInstance);

        Assert.Equal(typeof(object), field.FieldType);
    }

    [Fact]
    public void NativeAdapterProperty_ResolvesToIDXGIAdapter1() =>
        EngineReflectionPin.RequirePropertyOfType(
            typeof(GraphicsAdapter), "NativeAdapter", NonPublicInstance, typeof(ComPtr<IDXGIAdapter1>));

    [Fact]
    public void NativeResourceProperty_ResolvesToID3D12Resource() =>
        EngineReflectionPin.RequirePropertyOfType(
            typeof(GraphicsResourceBase), "NativeResource", NonPublicInstance, typeof(ComPtr<ID3D12Resource>));

    [Fact]
    public void LayoutTracker_GetAndSetTakeTheWholeResourceLayout()
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsResource), "LayoutTracker", NonPublicInstance);

        var get = EngineReflectionPin.RequireMethod(field.FieldType, "Get", PublicInstance);
        Assert.Equal([typeof(uint)], get.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(typeof(BarrierLayout), get.ReturnType);

        var set = EngineReflectionPin.RequireMethod(field.FieldType, "Set", PublicInstance);
        Assert.Equal([typeof(uint), typeof(BarrierLayout)], set.GetParameters().Select(p => p.ParameterType));
    }

    /// <summary>
    /// The adapter compiles against the Direct3D11 build of Stride.Graphics, and casts the boxed
    /// D3D12 <see cref="BarrierLayout"/> it reads to that build's enum. Same values in both, today.
    /// </summary>
    [Fact]
    public void BarrierLayout_HasTheValuesTheAdapterWasCompiledAgainst()
    {
        Assert.Equal(1, (int)BarrierLayout.Common);
        Assert.Equal(2, (int)BarrierLayout.RenderTarget);
        Assert.Equal(5, (int)BarrierLayout.ShaderResource);
        Assert.Equal(7, (int)BarrierLayout.CopySource);
        Assert.Equal(8, (int)BarrierLayout.CopyDest);
    }

    /// <summary>The adapter passes <c>(uint)texture.Format</c> to Skia as the <c>DXGI_FORMAT</c>.</summary>
    [Fact]
    public void PixelFormat_ValuesAreDxgiFormats()
    {
        Assert.Equal(D3D12Constants.FormatR8G8B8A8Unorm, (uint)PixelFormat.R8G8B8A8_UNorm);
        Assert.Equal(D3D12Constants.FormatB8G8R8A8Unorm, (uint)PixelFormat.B8G8R8A8_UNorm);
        Assert.Equal(D3D12Constants.FormatR10G10B10A2Unorm, (uint)PixelFormat.R10G10B10A2_UNorm);
        Assert.Equal(D3D12Constants.FormatR16G16B16A16Unorm, (uint)PixelFormat.R16G16B16A16_UNorm);
    }
}
