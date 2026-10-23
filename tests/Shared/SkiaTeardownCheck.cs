using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;

namespace Tests.Shared;

/// <summary>
/// Skia's GL objects have to be freed on Skia's own context. Freed on the engine's context instead,
/// deleting an FBO rebinds the engine context's framebuffer behind the engine's binding cache, and where
/// the driver names FBOs per context (GL says they are not shared; Mesa and NVIDIA share them anyway) it
/// deletes the engine's own FBO with the same name. Linked into every engine GL test project that runs a
/// <see cref="OneFrameGame"/>.
/// </summary>
static class SkiaTeardownCheck
{
    const int Count = 8;
    const int Size = 16;

    /// <summary>
    /// Disposes Skia targets, then the renderer, each while an engine render target is bound, and checks
    /// that a clear issued right after still lands in that target. After each step every other engine
    /// target must also keep its contents and take a fresh clear. Returns every failure, or "".
    /// </summary>
    internal static string EngineTargetsSurvive(GraphicsDevice device)
    {
        using var bound = new RenderTarget2D(device, Size, Size);
        var engineTargets = new RenderTarget2D[Count];
        for (var i = 0; i < Count; i++)
            engineTargets[i] = new RenderTarget2D(device, Size, Size);
        try
        {
            // Binding a target is what makes the engine create its FBO.
            for (var i = 0; i < Count; i++)
            {
                device.SetRenderTarget(engineTargets[i]);
                device.Clear(ColorFor(i, 0));
            }
            device.SetRenderTarget(null);

            var skiaTargets = new SkiaRenderTarget2D[Count];
            for (var i = 0; i < Count; i++)
            {
                skiaTargets[i] = new SkiaRenderTarget2D(device, Size, Size);
                skiaTargets[i].Begin();
                skiaTargets[i].Canvas.Clear(SKColors.Red);
                skiaTargets[i].EndWithoutDrawing();
            }

            // The cached direct-draw surfaces go when the renderer does.
            if (SkiaRenderer.SupportsDirectCanvas)
            {
                using var direct = new SkiaDirectCanvas(device);
                direct.Begin();
                using var paint = new SKPaint { Color = SKColors.Red };
                direct.Canvas.DrawRect(0, 0, 4, 4, paint);
                direct.End();
            }

            var errors = new List<string>();
            Step(device, bound, engineTargets, 1, "disposing the Skia targets", errors, () =>
            {
                foreach (var target in skiaTargets)
                    target.Dispose();
            });
            Step(device, bound, engineTargets, 2, "SkiaRenderer.Dispose", errors, SkiaRenderer.Dispose);
            return string.Join("; ", errors);
        }
        finally
        {
            device.SetRenderTarget(null);
            foreach (var target in engineTargets)
                target.Dispose();
        }
    }

    static void Step(GraphicsDevice device, RenderTarget2D bound, RenderTarget2D[] targets, int round, string step,
        List<string> errors, Action action)
    {
        device.SetRenderTarget(bound);
        action();
        device.Clear(ColorFor(Count, round));
        device.SetRenderTarget(null);

        var boundColor = ReadCenter(bound);
        if (boundColor != ColorFor(Count, round))
            errors.Add($"the bound engine target missed a clear issued after {step} ({boundColor}, expected {ColorFor(Count, round)})");

        for (var i = 0; i < targets.Length; i++)
        {
            var kept = ReadCenter(targets[i]);
            if (kept != ColorFor(i, round - 1))
                errors.Add($"engine target {i} lost its contents after {step} ({kept}, expected {ColorFor(i, round - 1)})");

            device.SetRenderTarget(targets[i]);
            device.Clear(ColorFor(i, round));
            device.SetRenderTarget(null);

            var cleared = ReadCenter(targets[i]);
            if (cleared != ColorFor(i, round))
                errors.Add($"engine target {i} ignored a clear after {step} ({cleared}, expected {ColorFor(i, round)})");
        }
    }

    static Color ReadCenter(RenderTarget2D target)
    {
        var pixels = new Color[Size * Size];
        target.GetData(pixels);
        return pixels[Size / 2 * Size + Size / 2];
    }

    static Color ColorFor(int index, int round) => new(20 + index * 25, 40 + round * 60, 200 - index * 10, 255);
}
