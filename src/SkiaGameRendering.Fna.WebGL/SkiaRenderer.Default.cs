namespace SkiaGameRendering
{
    public static partial class SkiaRenderer
    {
#pragma warning disable CA1416 // this assembly only ever runs in a browser
        internal static partial SkiaBackend CreateDefaultBackend() => new SkiaFnaWebGlBackend();
#pragma warning restore CA1416
    }
}
