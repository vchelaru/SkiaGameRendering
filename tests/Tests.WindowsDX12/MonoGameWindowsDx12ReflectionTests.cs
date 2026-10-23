using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Tests.Shared;
using Xunit;
using Xunit.Sdk;

namespace Tests.WindowsDX12;

/// <summary>
/// Pins what <c>SkiaDx12Backend</c>'s SkiaDirectCanvas support reaches in MonoGame by name or by memory layout
/// (src/SkiaGameRendering.WindowsDX12/MonoGameD3D12Native.cs). A MonoGame bump that moves any of it leaves
/// <c>dotnet build</c> green and would corrupt a frame, so each failure here names what changed. The runtime
/// layout itself is checked against a live device by the sample's direct smoke test in CI.
///
/// The names are duplicated from the backend on purpose: it only resolves them while initializing against a
/// live GraphicsDevice, which needs a D3D12 device and a window.
/// </summary>
public sealed class MonoGameWindowsDx12ReflectionTests
{
    const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The native device pointer every layout read starts from.</summary>
    [Fact]
    public void GraphicsDeviceHandle_IsAPointerField()
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsDevice), "Handle", NonPublicInstance);
        Assert.True(field.FieldType.IsPointer, $"GraphicsDevice.Handle is {field.FieldType}, expected a pointer.");
    }

    /// <summary>
    /// The managed state flags raised after a cut so MonoGame re-sends the blend factor and stencil reference,
    /// which it records straight onto its command list.
    /// </summary>
    [Theory]
    [InlineData("_blendStateDirty")]
    [InlineData("_blendFactorDirty")]
    [InlineData("_depthStencilStateDirty")]
    public void GraphicsDeviceDirtyFlag_Resolves(string name)
    {
        var field = EngineReflectionPin.RequireField(typeof(GraphicsDevice), name, NonPublicInstance);
        Assert.Equal(typeof(bool), field.FieldType);
    }

    /// <summary>
    /// The mgruntime.dll in MonoGame.Runtime.Windows.DX12 is one whose native layout was verified. Bumping the
    /// package fails this until the new build has been diffed against the struct offsets in MonoGameD3D12Native
    /// and its hash added there.
    /// </summary>
    [Fact]
    public void ShippedNativeRuntime_IsAVerifiedBuild()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "mgruntime.dll");
        if (!File.Exists(path))
            throw new XunitException($"{path} is missing; the MonoGame.Runtime.Windows.DX12 package did not copy its native runtime.");

        var hash = MonoGameD3D12Native.HashFile(path);
        Assert.True(MonoGameD3D12Native.VerifiedVersion(hash) != null,
            $"mgruntime.dll (SHA-256 {hash}) is not a verified MonoGame build. Re-check the offsets in MonoGameD3D12Native " +
            "against the new native/monogame/directx12 sources, probe a live device, then add the hash.");
    }
}
