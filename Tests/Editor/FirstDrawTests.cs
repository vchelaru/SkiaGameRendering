using NUnit.Framework;

namespace SkiaGameRendering.Tests
{
    /// <summary>
    /// The first draw into a freshly created <see cref="SkiaRenderTarget2D"/> must reach the texture.
    /// On Direct3D 12 it was lost when the draw ran on Unity's default plugin event, where Skia's submit
    /// could pass Unity's own unsubmitted first-use work on the new texture (#326); only a draw after a
    /// readback showed up. Every target here is new and read back with no earlier draw or readback on it,
    /// so a lost first draw leaves it transparent. It runs on whichever graphics API the Editor uses
    /// (pass <c>-force-d3d11</c>, <c>-force-d3d12</c>, <c>-force-vulkan</c> or <c>-force-glcore</c>).
    /// </summary>
    public sealed class FirstDrawTests
    {
        [Test]
        public void FirstDrawIntoFreshTargetIsVisible()
        {
            // Each pass creates, draws and reads back one new texture, so none has a draw behind it.
            for (int i = 0; i < 5; i++)
                DomainReloadTests.DrawAndCheck();
        }
    }
}
