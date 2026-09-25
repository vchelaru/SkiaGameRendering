namespace SkiaGameRendering
{
    public static partial class SkiaRenderer
    {
        // SkiaWebGlBackend needs a host, so there is no default to construct; AttachHost supplies
        // the ambient factory Initialize(GraphicsDevice) uses instead.
        internal static partial SkiaBackend CreateDefaultBackend() =>
            throw new InvalidOperationException(
                "SkiaWebGlBackend needs a host. Call SkiaRenderer.AttachHost before " +
                "SkiaRenderer.Initialize(GraphicsDevice), or construct the backend explicitly and pass " +
                "it to SkiaRenderer.Initialize(SkiaBackend, GraphicsDevice).");
    }
}
