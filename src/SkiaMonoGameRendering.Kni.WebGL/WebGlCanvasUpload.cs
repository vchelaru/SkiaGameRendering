using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Platform.Graphics;

namespace SkiaMonoGameRendering.Kni.WebGL;

public enum WebGLCanvasUploadMode
{
    TexSubImage2D = 0,
    TexImage2D = 1,
}

public enum WebGLCanvasColorSpaceConversion
{
    BrowserDefault = 0,
    None = 1,
}

public readonly struct BrowserCanvasSource
{
    public BrowserCanvasSource(string elementId, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(elementId))
            throw new ArgumentException("A canvas element id is required.", nameof(elementId));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        ElementId = elementId;
        Width = width;
        Height = height;
    }

    public string ElementId { get; }
    public int Width { get; }
    public int Height { get; }
}

public struct CanvasUploadOptions
{
    public bool FlipY { get; set; }
    public bool PremultiplyAlpha { get; set; }
    public WebGLCanvasColorSpaceConversion ColorSpaceConversion { get; set; }
    public WebGLCanvasUploadMode UploadMode { get; set; }
    public bool ValidateDimensions { get; set; }

    public static CanvasUploadOptions Default => new CanvasUploadOptions
    {
        ValidateDimensions = true,
        UploadMode = WebGLCanvasUploadMode.TexSubImage2D,
        ColorSpaceConversion = WebGLCanvasColorSpaceConversion.BrowserDefault,
    };
}

[SupportedOSPlatform("browser")]
public static class WebGLTexture2DExtensions
{
    public static void UploadFromCanvas(this Texture2D destination, BrowserCanvasSource source) =>
        UploadFromCanvas(destination, source, CanvasUploadOptions.Default);

    public static void UploadFromCanvas(this Texture2D destination, BrowserCanvasSource source, CanvasUploadOptions options)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        if (destination.IsDisposed)
            throw new ObjectDisposedException(nameof(destination));
        if (destination.Format != SurfaceFormat.Color)
            throw new NotSupportedException("Canvas upload supports SurfaceFormat.Color only.");
        if (options.ValidateDimensions &&
            (destination.Width != source.Width || destination.Height != source.Height))
            throw new ArgumentException("Source canvas dimensions must match the destination texture.", nameof(source));

        KniWebGlInternals.Upload(destination, source, options);
    }
}

// KNI's BlazorGL backend has no public API for "give me the native WebGLTexture/WebGL-context handle
// behind this Texture2D" - the pieces below are reached via reflection into KNI internals instead of
// forking/patching KNI's source (see eng/Versions.props history and repo issue #2 for the prior patch
// this replaced).
//
// Status as of KNI v4.3.9001 (kniEngine/kni#2669, authored upstream from this repo):
//   - Texture2D._strategyTexture2D -> ConcreteTexture2D._glTexture is now ALSO reachable publicly via
//     Texture2D.GetSharedHandle(), but only for textures created with shared=true, and no public
//     Texture2D constructor exposes that flag (only RenderTarget2D does). Reflecting the field directly
//     avoids forcing every canvas-upload target to become a RenderTarget2D just to get a handle.
//   - The current WebGL rendering context (needed so the JS side can bind/upload into KNI's texture)
//     is still fully internal (ConcreteGraphicsContext.GL) - #2669 did not cover this. A follow-up
//     upstream PR mirroring #2669's shape is the long-term fix; until then this is genuinely
//     internals-only, not a case of avoidable reflection.
//
// If KNI renames/restructures these members, the MissingFieldException/MissingMemberException below
// will fail loudly at first use rather than silently misbehaving.
[SupportedOSPlatform("browser")]
internal static class KniWebGlInternals
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly FieldInfo StrategyTexture2DField =
        typeof(Texture2D).GetField("_strategyTexture2D", NonPublicInstance)
        ?? throw new MissingFieldException("Texture2D._strategyTexture2D was not found; KNI's internal layout changed.");

    private static FieldInfo? _glTextureField;
    private static PropertyInfo? _glContextProperty;

    internal static void Upload(Texture2D destination, BrowserCanvasSource source, CanvasUploadOptions options)
    {
        var graphicsDevice = destination.GraphicsDevice;
        var contextUid = GetGlContextUid(graphicsDevice);
        var textureUid = GetGlTextureUid(destination);

        // Binding/uploading into KNI's texture behind its back invalidates KNI's own cached notion of
        // what's bound to texture unit 0; Dirty(0) forces it to rebind next time it draws. This part
        // (unlike the reflection above) is already public API.
        ((IPlatformTextureCollection)graphicsDevice.VertexTextures).Strategy.Dirty(0);
        ((IPlatformTextureCollection)graphicsDevice.Textures).Strategy.Dirty(0);

        CanvasTextureUploadInterop.Upload(
            contextUid, textureUid, source.ElementId,
            options.FlipY, options.PremultiplyAlpha,
            options.ColorSpaceConversion == WebGLCanvasColorSpaceConversion.None,
            options.UploadMode == WebGLCanvasUploadMode.TexImage2D);
    }

    private static int GetGlTextureUid(Texture2D texture)
    {
        var strategy = StrategyTexture2DField.GetValue(texture)
            ?? throw new InvalidOperationException("Texture2D has no backing strategy.");

        _glTextureField ??= strategy.GetType().GetField("_glTexture", NonPublicInstance)
            ?? throw new MissingFieldException("ConcreteTexture2D._glTexture was not found; KNI's internal layout changed.");

        var glTexture = _glTextureField.GetValue(strategy)
            ?? throw new ObjectDisposedException(nameof(texture));

        return GetUid(glTexture);
    }

    private static int GetGlContextUid(GraphicsDevice graphicsDevice)
    {
        var currentContext = ((IPlatformGraphicsDevice)graphicsDevice).Strategy.CurrentContext;
        var contextStrategy = ((IPlatformGraphicsContext)currentContext).Strategy;

        _glContextProperty ??= contextStrategy.GetType().GetProperty("GL", NonPublicInstance)
            ?? throw new MissingMemberException("ConcreteGraphicsContext.GL was not found; KNI's internal layout changed.");

        var glContext = _glContextProperty.GetValue(contextStrategy)
            ?? throw new InvalidOperationException("The WebGL context is unavailable.");

        return GetUid(glContext);
    }

    // WebGLTexture.Uid / RenderingContext.Uid (nkast.Wasm.Canvas.WebGL) are public, but this project
    // doesn't reference that package directly - it stays Blazor-agnostic at compile time - so the Uid
    // is read by name instead of adding a browser-specific dependency just for one int property.
    private static int GetUid(object nkastWasmObject)
    {
        var uidProperty = nkastWasmObject.GetType().GetProperty("Uid", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMemberException($"{nkastWasmObject.GetType().Name}.Uid was not found.");
        return (int)uidProperty.GetValue(nkastWasmObject)!;
    }
}

[SupportedOSPlatform("browser")]
internal static partial class CanvasTextureUploadInterop
{
    [JSImport("globalThis.skiaMonoGameWebGl.uploadFromCanvas")]
    internal static partial void Upload(
        int contextUid, int textureUid, string sourceElementId,
        bool flipY, bool premultiplyAlpha, bool disableColorSpaceConversion, bool useTexImage);
}
