#nullable enable
using System;

namespace SkiaGameRendering
{
    /// <summary>Package-wide facts, mirroring the <c>SkiaRenderer</c> of the other engines. Unity needs no initialization.</summary>
    public static class SkiaRenderer
    {
        /// <summary>
        /// Always false: <see cref="SkiaRenderTarget2D"/>'s <c>multiSampleCount</c> exists for a portable API, but only 0
        /// works here and 2, 4 and 8 throw <see cref="NotSupportedException"/>. Needs no initialization to answer.
        /// </summary>
        public static bool SupportsMultiSampledTargets => false;
    }
}
