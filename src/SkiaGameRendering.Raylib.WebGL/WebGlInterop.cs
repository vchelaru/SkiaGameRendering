using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace SkiaGameRendering;

/// <summary>
/// The JavaScript half (native/skia-raylib-webgl.js, linked into dotnet.native.js with --pre-js). It runs inside
/// the Emscripten module raylib and SkiaSharp are both linked into, so it reaches raylib's WebGL context directly.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class WebGlInterop
{
    // [contextHandle, isWebGl2, antialias, stencil, alpha]
    [JSImport("globalThis.skiaRaylibWebGl.attach")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial int[] Attach();

    [JSImport("globalThis.skiaRaylibWebGl.isContextLost")]
    internal static partial bool IsContextLost();

    // [framebufferId, drawingBufferWidth, drawingBufferHeight, samples, stencilBits, attachmentKind]
    [JSImport("globalThis.skiaRaylibWebGl.beginPass")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial int[] BeginPass();

    [JSImport("globalThis.skiaRaylibWebGl.endPass")]
    internal static partial void EndPass();

    // [framebufferId, renderbufferHandle]
    [JSImport("globalThis.skiaRaylibWebGl.createAttachmentFramebuffer")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial int[] CreateAttachmentFramebuffer(int width, int height);

    [JSImport("globalThis.skiaRaylibWebGl.createTextureFramebuffer")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    internal static partial int[] CreateTextureFramebuffer(int textureId, int width, int height);

    [JSImport("globalThis.skiaRaylibWebGl.deleteFramebuffer")]
    internal static partial void DeleteFramebuffer(int framebufferId, int renderbufferHandle);

    [JSImport("globalThis.skiaRaylibWebGl.glErrorCount")]
    internal static partial int GlErrorCount();

    [JSImport("globalThis.skiaRaylibWebGl.glErrorLog")]
    internal static partial string GlErrorLog();
}
