using SkiaGameRendering.Kni.DesktopGL;

namespace SkiaGameRendering
{
    public static partial class SkiaRenderer
    {
        internal static partial SkiaBackend CreateDefaultBackend() => new SkiaKniGlBackend();
    }
}
