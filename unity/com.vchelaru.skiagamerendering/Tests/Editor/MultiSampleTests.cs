using System;
using NUnit.Framework;

namespace SkiaGameRendering.Tests
{
    /// <summary>
    /// Unity cannot back a multisampled render target, so the flag is false and every non-zero
    /// <c>multiSampleCount</c> throws instead of falling back to 0.
    /// </summary>
    public sealed class MultiSampleTests
    {
        [Test]
        public void FlagIsFalse() => Assert.That(SkiaRenderer.SupportsMultiSampledTargets, Is.False);

        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(-1)]
        [TestCase(16)]
        public void NonZeroCountThrowsNotSupported(int count) =>
            Assert.Throws<NotSupportedException>(() => new SkiaRenderTarget2D(16, 16, multiSampleCount: count));

        [Test]
        public void ZeroCountWorks()
        {
            using var target = new SkiaRenderTarget2D(16, 16, multiSampleCount: 0);
            Assert.That(target.Texture.antiAliasing, Is.EqualTo(1));
        }
    }
}
