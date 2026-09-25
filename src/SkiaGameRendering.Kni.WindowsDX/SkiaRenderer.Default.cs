using SkiaGameRendering.Kni.WindowsDX;

namespace SkiaGameRendering
{
    public static partial class SkiaRenderer
    {
        internal static partial SkiaBackend CreateDefaultBackend() => new SkiaKniAngleBackend();
    }
}
