using Microsoft.Xna.Framework.Graphics;

namespace Benchmarks.DirectDraw;

/// <summary>
/// Stand-in for the DesktopGL benchmark's GPU timing: D3D11 timestamp queries are not wired up, so the GPU
/// columns of a WindowsDX result are NaN.
/// </summary>
sealed class GpuTimestamps
{
    internal static string Renderer() => "D3D11 via ANGLE, " + GraphicsAdapter.DefaultAdapter.Description;

    internal void Begin(int frame) { }

    internal void End(int frame) { }

    internal double? ReadLagged(int frame) => null;
}
