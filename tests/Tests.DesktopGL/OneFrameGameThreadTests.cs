using SkiaGameRendering;
using Tests.Shared;
using Xunit;

namespace Tests.DesktopGL;

/// <summary>
/// MonoGame throws from every device call off the thread that first touched it, and xUnit picks the
/// thread for each test class. Rendering from two fresh threads fails every time unless
/// <see cref="OneFrameGame"/> keeps every game on one thread of its own.
/// </summary>
public sealed class OneFrameGameThreadTests
{
    [Fact]
    public void Render_FromTwoDifferentThreads_BothSucceed()
    {
        RenderOnFreshThread();
        RenderOnFreshThread();
    }

    static void RenderOnFreshThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { OneFrameGame.Render(() => new SkiaGlBackend(), _ => string.Empty); }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start();
        thread.Join();

        if (failure != null)
            throw new InvalidOperationException("Rendering from a fresh thread failed.", failure);
    }
}
