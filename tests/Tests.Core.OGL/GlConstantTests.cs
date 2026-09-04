using System.Reflection;
using SkiaGameRendering.Core.OGL;
using Xunit;

namespace Tests.CoreOgl;

/// <summary>
/// Pins every value <c>GlConstants</c> declares to its official registry value.
///
/// These are hand-transcribed hex tokens, so a wrong digit compiles, packs and publishes cleanly and
/// only shows up as a black or corrupt render on real hardware.
///
/// Expected values come from KhronosGroup/OpenGL-Registry: <c>api/GL/glcorearb.h</c>,
/// <c>api/GL/glext.h</c> (the <c>*Ext</c> spellings) and <c>api/GLES2/gl2ext.h</c> (the
/// <c>*Oes</c> spellings).
/// </summary>
public sealed class GlConstantTests
{
    /// <summary>
    /// Keyed by declaring type: bare name for <c>GlConstants</c>' own constants, <c>Enum.Member</c>
    /// for enum members. The value is the registry value; the comment is the registry token, which
    /// is what makes a wrong transcription visible - several members here are spelled nothing like
    /// the token they carry.
    /// </summary>
    private static readonly Dictionary<string, int> Expected = new()
    {
        ["GL_SAMPLES"] = 0x80A9,

        // GL_RENDERBUFFER / GL_RENDERBUFFER_EXT
        ["RenderbufferTarget.Renderbuffer"] = 0x8D41,
        ["RenderbufferTarget.RenderbufferExt"] = 0x8D41,

        // GL_FRAMEBUFFER / GL_FRAMEBUFFER_EXT / GL_READ_FRAMEBUFFER
        ["FramebufferTarget.Framebuffer"] = 0x8D40,
        ["FramebufferTarget.FramebufferExt"] = 0x8D40,
        ["FramebufferTarget.ReadFramebuffer"] = 0x8CA8,

        // GL_RGBA8, GL_DEPTH_COMPONENT16/24, GL_DEPTH24_STENCIL8, the OES spellings of the last
        // two, and GL_STENCIL_INDEX8.
        ["RenderbufferStorage.Rgba8"] = 0x8058,
        ["RenderbufferStorage.DepthComponent16"] = 0x81A5,
        ["RenderbufferStorage.DepthComponent24"] = 0x81A6,
        ["RenderbufferStorage.Depth24Stencil8"] = 0x88F0,
        ["RenderbufferStorage.DepthComponent24Oes"] = 0x81A6,
        ["RenderbufferStorage.Depth24Stencil8Oes"] = 0x88F0,
        ["RenderbufferStorage.StencilIndex8"] = 0x8D48,

        // GL_COLOR_ATTACHMENT0, GL_DEPTH_ATTACHMENT, GL_STENCIL_ATTACHMENT. The three "...Ext"
        // members are not attachment points at all: they carry GL_COLOR / GL_DEPTH / GL_STENCIL,
        // the buffer names glClearBuffer and friends take. Nothing references them.
        ["FramebufferAttachment.ColorAttachment0"] = 0x8CE0,
        ["FramebufferAttachment.ColorAttachment0Ext"] = 0x8CE0,
        ["FramebufferAttachment.DepthAttachment"] = 0x8D00,
        ["FramebufferAttachment.StencilAttachment"] = 0x8D20,
        ["FramebufferAttachment.ColorAttachmentExt"] = 0x1800,
        ["FramebufferAttachment.DepthAttachementExt"] = 0x1801,
        ["FramebufferAttachment.StencilAttachmentExt"] = 0x1802,

        // GL_TEXTURE_2D / _3D / _CUBE_MAP and the six cube-map faces.
        ["TextureTarget.Texture2D"] = 0x0DE1,
        ["TextureTarget.Texture3D"] = 0x806F,
        ["TextureTarget.TextureCubeMap"] = 0x8513,
        ["TextureTarget.TextureCubeMapPositiveX"] = 0x8515,
        ["TextureTarget.TextureCubeMapNegativeX"] = 0x8516,
        ["TextureTarget.TextureCubeMapPositiveY"] = 0x8517,
        ["TextureTarget.TextureCubeMapNegativeY"] = 0x8518,
        ["TextureTarget.TextureCubeMapPositiveZ"] = 0x8519,
        ["TextureTarget.TextureCubeMapNegativeZ"] = 0x851A,

        // glCheckFramebufferStatus return values. The EXT pairs are the ARB and EXT spellings of
        // one token; IncompleteDimensionsExt and IncompleteFormatsExt exist only in EXT.
        ["FramebufferErrorCode.FramebufferUndefined"] = 0x8219,
        ["FramebufferErrorCode.FramebufferComplete"] = 0x8CD5,
        ["FramebufferErrorCode.FramebufferCompleteExt"] = 0x8CD5,
        ["FramebufferErrorCode.FramebufferIncompleteAttachment"] = 0x8CD6,
        ["FramebufferErrorCode.FramebufferIncompleteAttachmentExt"] = 0x8CD6,
        ["FramebufferErrorCode.FramebufferIncompleteMissingAttachment"] = 0x8CD7,
        ["FramebufferErrorCode.FramebufferIncompleteMissingAttachmentExt"] = 0x8CD7,
        ["FramebufferErrorCode.FramebufferIncompleteDimensionsExt"] = 0x8CD9,
        ["FramebufferErrorCode.FramebufferIncompleteFormatsExt"] = 0x8CDA,
        ["FramebufferErrorCode.FramebufferIncompleteDrawBuffer"] = 0x8CDB,
        ["FramebufferErrorCode.FramebufferIncompleteDrawBufferExt"] = 0x8CDB,
        ["FramebufferErrorCode.FramebufferIncompleteReadBuffer"] = 0x8CDC,
        ["FramebufferErrorCode.FramebufferIncompleteReadBufferExt"] = 0x8CDC,
        ["FramebufferErrorCode.FramebufferUnsupported"] = 0x8CDD,
        ["FramebufferErrorCode.FramebufferUnsupportedExt"] = 0x8CDD,
        ["FramebufferErrorCode.FramebufferIncompleteMultisample"] = 0x8D56,
        ["FramebufferErrorCode.FramebufferIncompleteLayerTargets"] = 0x8DA8,
        ["FramebufferErrorCode.FramebufferIncompleteLayerCount"] = 0x8DA9,
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
        var declared = DeclaredConstants();
        Assert.True(declared.ContainsKey(name), $"GlConstants no longer declares {name}.");

        Assert.Equal(expected, declared[name]);
    }

    /// <summary>
    /// Fails when a constant is added to <c>GlConstants</c> without a registry value to check it
    /// against, so this file cannot silently fall behind the one it is pinning.
    /// </summary>
    [Fact]
    public void EveryDeclaredConstant_IsPinned()
    {
        var unpinned = DeclaredConstants().Keys
            .Where(name => !Expected.ContainsKey(name))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(unpinned.Length == 0,
            "GlConstants declares values with no pinned registry value: " + string.Join(", ", unpinned));
    }

    /// <summary>
    /// Every value <c>GlConstants</c> declares, as name -> value. Bare name for the class' own
    /// constants, <c>Enum.Member</c> for enum members.
    /// </summary>
    private static Dictionary<string, int> DeclaredConstants()
    {
        var glConstants = typeof(GlConstants);

        var declared = Literals(glConstants)
            .ToDictionary(f => f.Name, f => Convert.ToInt32(f.GetRawConstantValue()));

        foreach (var nested in glConstants.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (!nested.IsEnum)
                continue;

            foreach (var field in Literals(nested))
                declared.Add($"{nested.Name}.{field.Name}", Convert.ToInt32(field.GetRawConstantValue()));
        }

        return declared;
    }

    private static IEnumerable<FieldInfo> Literals(Type type) =>
        type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly);
}
