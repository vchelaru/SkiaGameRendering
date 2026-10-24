using Android.Util;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using Tests.Shared;

namespace Sample.Shared
{
    /// <summary>The Android samples' (KNI and MonoGame) extra smoke-test check, logged under the "SkiaSample" tag.</summary>
    static class AndroidDirectCanvasCheck
    {
        /// <summary>
        /// <see cref="SkiaDirectCanvas"/> layered between SpriteBatch draws, on the back buffer, a plain
        /// render target and a multisampled one. The multisampled case is skipped on a device without
        /// MSAA render targets (GLES 2), and the log says so. <paramref name="msaaSkipReason"/> skips it too,
        /// for an engine that cannot resolve a multisampled target on GLES at all.
        /// </summary>
        public static bool Run(GraphicsDevice graphicsDevice, string msaaSkipReason = null)
        {
            var passed = true;
            void Check(string what, string errors)
            {
                Log.Info("SkiaSample", $"Direct canvas, {what}: {(errors == "" ? "passed" : "FAILED: " + errors)}");
                passed &= errors == "";
            }

            Check("back buffer", DirectDrawLayering.BackBuffer(graphicsDevice));
            Check("render target", DirectDrawLayering.RenderTarget(graphicsDevice, 0));

            if (msaaSkipReason != null)
            {
                Log.Info("SkiaSample", $"Direct canvas, 4x MSAA render target: skipped, {msaaSkipReason}");
                return passed;
            }

            int samples;
            using (var probe = new RenderTarget2D(graphicsDevice, 1, 1, false, SurfaceFormat.Color, DepthFormat.None, 4,
                       RenderTargetUsage.DiscardContents))
                samples = probe.MultiSampleCount;
            if (samples == 4)
                Check("4x MSAA render target", DirectDrawLayering.RenderTarget(graphicsDevice, 4));
            else
                Log.Info("SkiaSample", $"Direct canvas, 4x MSAA render target: skipped, the device made {samples}x");

            return passed;
        }
    }
}
