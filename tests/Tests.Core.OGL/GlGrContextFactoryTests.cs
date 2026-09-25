using SkiaGameRendering.Core.OGL;
using Xunit;

namespace Tests.CoreOgl;

/// <summary>
/// Pins which GL_VERSION strings count as pre-3.0, where Skia's path renderers emit gl_VertexID
/// shaders the driver can't compile.
/// </summary>
public sealed class GlGrContextFactoryTests
{
    [Theory]
    [InlineData("2.1 Metal - 90.5", true)]
    [InlineData("2.1 INTEL-20.6.4", true)]
    [InlineData("1.5", true)]
    [InlineData("3.0 Mesa 23.1", false)]
    [InlineData("4.1 Metal - 90.5", false)]
    [InlineData("4.6.0 NVIDIA 555.85", false)]
    [InlineData("10.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBelowGl3(string? version, bool expected)
    {
        Assert.Equal(expected, GlGrContextFactory.IsBelowGl3(version));
    }
}
