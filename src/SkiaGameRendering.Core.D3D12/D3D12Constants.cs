namespace SkiaGameRendering.Core.D3D12
{
    /// <summary>
    /// Raw D3D12 resource states, formats and flags host adapters need when describing a wrapped
    /// resource to <see cref="D3D12SkiaSurfaceFactory.CreateTextureState"/> or driving
    /// <see cref="D3D12ResourceTransitioner"/>. Named here so adapters do not each redeclare the
    /// same magic numbers; Core.D3D12 itself still speaks raw <c>uint</c>s (no interop library on
    /// either side - see <see cref="D3D12SkiaSurfaceFactory"/>'s maintenance notes).
    /// </summary>
    public static class D3D12Constants
    {
        public const uint ResourceStateCommon = 0;
        public const uint ResourceStateRenderTarget = 0x4;
        public const uint ResourceStateNonPixelShaderResource = 0x40;
        public const uint ResourceStatePixelShaderResource = 0x80;
        public const uint ResourceStateAllShaderResource = ResourceStateNonPixelShaderResource | ResourceStatePixelShaderResource;
        public const uint ResourceStateCopyDest = 0x400;
        public const uint ResourceStateCopySource = 0x800;

        public const uint FormatR16G16B16A16Unorm = 11;
        public const uint FormatR10G10B10A2Unorm = 24;
        public const uint FormatR8G8B8A8Unorm = 28;
        public const uint FormatB8G8R8A8Unorm = 87;
    }
}
