using SkiaSharp;

namespace SkiaGameRendering.Core.OGL
{
    /// <summary>
    /// The Skia surfaces a direct canvas wraps around an app's FBOs, kept across passes. Each is an FBO of Skia's own
    /// around the app's color attachment, keyed on the app's FBO, the attachment's GL name, type and sample count, and
    /// the target size. A hit attaches the color again, so an app that deletes a texture or renderbuffer and gets the
    /// same GL name for a new one still draws into the new one; no invalidate call is needed. Holds at most
    /// <see cref="MaxEntries"/> surfaces and drops them all when a new one would go past that, so an app cycling many
    /// FBOs frees as it goes. All calls need Skia's GL context current.
    /// </summary>
    public sealed class GlAttachmentSurfaceCache
    {
        public const int MaxEntries = 8;

        readonly record struct Key(GlDrawTarget Target, int Width, int Height, int Samples);

        sealed class Entry
        {
            internal SKSurface Surface = null!;
            internal GRBackendRenderTarget RenderTarget = null!;
            internal GlFramebufferState Framebuffer = null!;
        }

        readonly Dictionary<Key, Entry> _entries = new();

        /// <summary>How many surfaces this cache has built, for tests.</summary>
        public int SurfacesCreated { get; private set; }

        public int Count => _entries.Count;

        /// <summary>
        /// The surface for <paramref name="target"/> at this size, built on a miss. Leaves Skia's FBO unbound; bind
        /// <paramref name="framebuffer"/> for drawing.
        /// </summary>
        public SKSurface Get(GRContext grContext, GlFunctions gl, GlDrawTarget target, int width, int height,
            GRSurfaceOrigin origin, out GlFramebufferState framebuffer)
        {
            var key = new Key(target, width, height, GlSkiaSurfaceFactory.QueryAttachmentSamples(gl, target));
            if (_entries.TryGetValue(key, out var cached))
            {
                GlSkiaSurfaceFactory.AttachColor(gl, cached.Framebuffer, target);
                framebuffer = cached.Framebuffer;
                return cached.Surface;
            }

            if (_entries.Count >= MaxEntries)
                Clear(gl);

            var (surface, renderTarget) = GlSkiaSurfaceFactory.CreateAttachmentSurface(
                grContext, gl, target, width, height, SKColorType.Rgba8888, out framebuffer, origin);
            _entries.Add(key, new Entry { Surface = surface, RenderTarget = renderTarget, Framebuffer = framebuffer });
            SurfacesCreated++;
            return surface;
        }

        /// <summary>
        /// Frees every surface and Skia's FBOs. Pass null for <paramref name="gl"/> once the context is gone, which
        /// frees the Skia objects and makes no GL call. The apps' textures and renderbuffers are kept.
        /// </summary>
        public void Clear(GlFunctions? gl)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Surface.Dispose();
                entry.RenderTarget.Dispose();
                if (gl != null)
                    GlSkiaSurfaceFactory.DisposeRenderState(gl, entry.Framebuffer, keepColor: true);
            }
            _entries.Clear();
        }
    }
}
