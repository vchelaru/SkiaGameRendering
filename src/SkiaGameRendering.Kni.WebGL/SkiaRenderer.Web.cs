using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering.Kni.WebGL;
using SkiaGameRendering.Kni.WebGL.Components;

namespace SkiaGameRendering
{
    /// <summary>
    /// WebGL-only extension of <see cref="SkiaRenderer"/>. Compiled only into
    /// <c>SkiaGameRendering.Kni.WebGL</c> (this file isn't linked into any other platform package),
    /// so it's the one place <see cref="SkiaRenderer"/>'s otherwise platform-agnostic source can
    /// reference a Blazor-specific type without breaking the other backends that share the rest of
    /// its source via linked-file includes.
    /// </summary>
    public static partial class SkiaRenderer
    {
        private static SkiaGameWebGlHost? _attachedHost;

        /// <summary>
        /// Attaches the page's <see cref="SkiaGameWebGlHost"/> so <see cref="Initialize(GraphicsDevice)"/>
        /// can construct a <see cref="SkiaWebGlBackend"/> from it once <see cref="IsReady"/> is true -
        /// for hosts (like XnaFiddle) that construct the consumer's <c>Game</c> internally, with no
        /// constructor hook to pass a backend through. Call once, page-lifetime, right after the
        /// host component mounts; safe to call again to attach a different host. Not called from
        /// <c>Game</c> code itself - this is harness/page wiring, same as rendering the host
        /// component markup in the first place. <c>Game</c> code only ever touches
        /// <see cref="IsReady"/> and <see cref="Initialize(GraphicsDevice)"/>, both declared on the
        /// shared, platform-agnostic part of <see cref="SkiaRenderer"/> - so that code compiles and
        /// behaves correctly unchanged if exported into a desktop project.
        /// </summary>
        /// <remarks>
        /// Deliberately not annotated <c>[SupportedOSPlatform("browser")]</c>, unlike
        /// <see cref="SkiaWebGlBackend"/>/<see cref="SkiaGameWebGlHost"/> themselves. That attribute
        /// propagates: every caller of an attributed member has to carry the same attribute (on the
        /// containing method or class) or the platform-compatibility analyzer flags the call site
        /// (CA1416). For an ordinary harness/page-wiring call like this one, that would force every
        /// consumer - e.g. XnaFiddle's own Blazor code-behind - to sprinkle
        /// <c>[SupportedOSPlatform("browser")]</c> just to call this method once, for no real safety
        /// benefit: this assembly (<c>SkiaGameRendering.Kni.WebGL</c>) only ever runs in a browser
        /// context in practice, regardless of its plain <c>net8.0</c> TFM, so there's no legitimate
        /// non-browser call path this attribute would actually be guarding against.
        /// </remarks>
        public static void AttachHost(SkiaGameWebGlHost host, SkiaWebGlOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(host);
            _attachedHost = host;
#pragma warning disable CA1416 // see <remarks> above - this assembly is browser-only in practice
            AttachAmbient(
                () => new SkiaWebGlBackend(_attachedHost!, options),
                () => _attachedHost?.IsReady ?? false);
#pragma warning restore CA1416
        }
    }
}
