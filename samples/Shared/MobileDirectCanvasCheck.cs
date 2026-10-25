using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Tests.Shared;

namespace SampleShared
{
    /// <summary>The extra smoke-test check of the Android (KNI and MonoGame) and iOS samples.</summary>
    static class MobileDirectCanvasCheck
    {
        /// <summary>
        /// <see cref="SkiaDirectCanvas"/> layered between SpriteBatch draws, on the back buffer, a plain
        /// render target and a multisampled one. The multisampled case is skipped on a device without
        /// MSAA render targets (GLES 2), and the log says so. <paramref name="msaaSkipReason"/> skips it too,
        /// for an engine that cannot resolve a multisampled target on GLES at all. Each result goes to <paramref name="log"/>.
        /// </summary>
        public static bool Run(GraphicsDevice graphicsDevice, System.Action<string> log, string msaaSkipReason = null)
        {
            var passed = true;
            void Check(string what, string errors)
            {
                log($"Direct canvas, {what}: {(errors == "" ? "passed" : "FAILED: " + errors)}");
                passed &= errors == "";
            }

            Check("back buffer", DirectDrawLayering.BackBuffer(graphicsDevice));
            Check("render target", DirectDrawLayering.RenderTarget(graphicsDevice, 0));

            if (msaaSkipReason != null)
            {
                log($"Direct canvas, 4x MSAA render target: skipped, {msaaSkipReason}");
                return passed;
            }

            int samples;
            using (var probe = new RenderTarget2D(graphicsDevice, 1, 1, false, SurfaceFormat.Color, DepthFormat.None, 4,
                       RenderTargetUsage.DiscardContents))
                samples = probe.MultiSampleCount;
            if (samples == 4)
                Check("4x MSAA render target", DirectDrawLayering.RenderTarget(graphicsDevice, 4));
            else
                log($"Direct canvas, 4x MSAA render target: skipped, the device made {samples}x");

            return passed;
        }
    }
}
