using SkiaGameRendering;

namespace SkiaGameRendering
{
    public static partial class SkiaRenderer
    {
        internal static partial SkiaBackend CreateDefaultBackend() => new SkiaAndroidBackend();
    }
}
