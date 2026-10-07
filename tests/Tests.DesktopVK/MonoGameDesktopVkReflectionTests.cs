using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Tests.Shared;
using Xunit;

namespace Tests.DesktopVK;

/// <summary>
/// Pins the private MonoGame member <c>SkiaVulkanBackend.DisposeTexture</c> reaches by string
/// (src/SkiaGameRendering.DesktopVK/SkiaVulkanBackend.cs). A MonoGame bump that renames it leaves
/// <c>dotnet build</c> green and breaks the backend at runtime. Change the name in both places.
/// </summary>
public sealed class MonoGameDesktopVkReflectionTests
{
    [Fact]
    public void PlatformApplyRenderTargets_Resolves()
    {
        var method = EngineReflectionPin.RequireMethod(
            typeof(GraphicsDevice), "PlatformApplyRenderTargets", BindingFlags.NonPublic | BindingFlags.Instance);
        EngineReflectionPin.RequireParameterCount(method, 0);
    }
}
