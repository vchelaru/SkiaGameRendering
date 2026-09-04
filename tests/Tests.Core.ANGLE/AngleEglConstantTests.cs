using System.Reflection;
using SkiaGameRendering.Core.ANGLE;
using Xunit;

namespace Tests.CoreAngle;

/// <summary>
/// Pins every integer constant <c>AngleEgl</c> declares to its official registry value.
///
/// These are hand-transcribed hex tokens, so a wrong digit compiles, packs and publishes cleanly and
/// only shows up as a wrong render on real hardware. Four of them were wrong when this test was
/// written (EGL_GREEN_SIZE, EGL_BLUE_SIZE, EGL_ALPHA_SIZE and EGL_HEIGHT), each colliding with
/// another token declared a few lines away.
///
/// Expected values come from:
///   EGL core         KhronosGroup/EGL-Registry     api/EGL/egl.h
///   EGL extensions   KhronosGroup/EGL-Registry     api/EGL/eglext.h
///   ANGLE extensions google/angle                  include/EGL/eglext_angle.h
///   GL (ES 3)        KhronosGroup/OpenGL-Registry  api/GLES3/gl3.h
/// </summary>
public sealed class AngleEglConstantTests
{
    private static readonly Dictionary<string, int> Expected = new()
    {
        ["EGL_NONE"] = 0x3038,
        ["EGL_TRUE"] = 1,
        ["EGL_FALSE"] = 0,
        ["EGL_SUCCESS"] = 0x3000,
        ["EGL_DEFAULT_DISPLAY"] = 0,

        ["EGL_RED_SIZE"] = 0x3024,
        ["EGL_GREEN_SIZE"] = 0x3023,
        ["EGL_BLUE_SIZE"] = 0x3022,
        ["EGL_ALPHA_SIZE"] = 0x3021,
        ["EGL_DEPTH_SIZE"] = 0x3025,
        ["EGL_STENCIL_SIZE"] = 0x3026,
        ["EGL_SURFACE_TYPE"] = 0x3033,
        ["EGL_RENDERABLE_TYPE"] = 0x3040,
        ["EGL_PBUFFER_BIT"] = 0x0001,
        ["EGL_OPENGL_ES2_BIT"] = 0x0004,
        // Core EGL spells this EGL_OPENGL_ES3_BIT_KHR; same value.
        ["EGL_OPENGL_ES3_BIT"] = 0x0040,

        ["EGL_CONTEXT_CLIENT_VERSION"] = 0x3098,

        ["EGL_PLATFORM_ANGLE_ANGLE"] = 0x3202,
        ["EGL_PLATFORM_ANGLE_TYPE_ANGLE"] = 0x3203,
        ["EGL_PLATFORM_ANGLE_TYPE_D3D11_ANGLE"] = 0x3208,
        ["EGL_PLATFORM_DEVICE_EXT"] = 0x313F,
        ["EGL_D3D11_DEVICE_ANGLE"] = 0x33A1,
        ["EGL_D3D_TEXTURE_ANGLE"] = 0x33A3,

        ["EGL_WIDTH"] = 0x3057,
        ["EGL_HEIGHT"] = 0x3056,
        ["EGL_TEXTURE_TARGET"] = 0x3081,
        ["EGL_TEXTURE_2D"] = 0x305F,
        ["EGL_TEXTURE_FORMAT"] = 0x3080,
        ["EGL_TEXTURE_RGBA"] = 0x305E,

        // From EGL_ANGLE_flexible_surface_compatibility, which ANGLE has since removed, so there is
        // no current header to check this against. Pinned at the value the repo has always used.
        // Nothing references it; it is a candidate for deletion.
        ["EGL_FLEXIBLE_SURFACE_COMPATIBILITY_SUPPORTED_ANGLE"] = 0x33A6,

        ["GL_FRAMEBUFFER"] = 0x8D40,
        ["GL_RENDERBUFFER"] = 0x8D41,
        ["GL_COLOR_ATTACHMENT0"] = 0x8CE0,
        ["GL_DEPTH_ATTACHMENT"] = 0x8D00,
        ["GL_STENCIL_ATTACHMENT"] = 0x8D20,
        ["GL_DEPTH24_STENCIL8"] = 0x88F0,
        ["GL_TEXTURE_2D"] = 0x0DE1,
        ["GL_FRAMEBUFFER_COMPLETE"] = 0x8CD5,
        ["GL_SAMPLES"] = 0x80A9,
        ["GL_TEXTURE_BINDING_2D"] = 0x8069,
    };

    public static TheoryData<string, int> ExpectedConstants()
    {
        var data = new TheoryData<string, int>();
        foreach (var pair in Expected)
            data.Add(pair.Key, pair.Value);
        return data;
    }

    [Theory]
    [MemberData(nameof(ExpectedConstants))]
    public void Constant_MatchesRegistryValue(string name, int expected)
    {
        var field = IntConstants().SingleOrDefault(f => f.Name == name);
        Assert.True(field != null, $"AngleEgl no longer declares {name}.");

        Assert.Equal(expected, (int)field!.GetRawConstantValue()!);
    }

    /// <summary>
    /// Fails when a constant is added to <c>AngleEgl</c> without a registry value to check it
    /// against, so this file cannot silently fall behind the one it is pinning.
    /// </summary>
    [Fact]
    public void EveryDeclaredConstant_IsPinned()
    {
        var unpinned = IntConstants()
            .Select(f => f.Name)
            .Where(name => !Expected.ContainsKey(name))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(unpinned.Length == 0,
            "AngleEgl declares constants with no pinned registry value: " + string.Join(", ", unpinned));
    }

    [Theory]
    [InlineData("EGL_NO_CONTEXT")]
    [InlineData("EGL_NO_DISPLAY")]
    [InlineData("EGL_NO_SURFACE")]
    public void NullHandle_IsZero(string name)
    {
        var field = typeof(AngleEgl).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field != null, $"AngleEgl no longer declares {name}.");

        Assert.Equal(IntPtr.Zero, (IntPtr)field!.GetValue(null)!);
    }

    private static IEnumerable<FieldInfo> IntConstants() =>
        typeof(AngleEgl)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(int));
}
