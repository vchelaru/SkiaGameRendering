using System.Diagnostics;
using SkiaSharp;
using Xunit;

namespace Tests.Godot;

/// <summary>
/// Launches the real Godot binary (<see cref="GodotBinaryFactAttribute"/>) against
/// <c>samples/Sample.Godot</c> on each supported rendering driver, with the sample's <c>--screenshot</c>
/// user argument, then checks the PNG it writes against the shared scene's layout (see
/// <c>samples/Shared/Scene.cs</c>): the red circle, the SVG drop, and the CornflowerBlue clear
/// everywhere else. Solid fills are driver-independent, unlike anti-aliased edges, which is why
/// this probes pixels instead of comparing a golden image (the window size also varies with the
/// host's display scaling).
/// </summary>
public class GodotSampleTests
{
    static readonly SKColor Red = new(255, 0, 0);
    static readonly SKColor CornflowerBlue = new(100, 149, 237);

    [GodotBinaryFact(Driver = "vulkan")]
    public void Vulkan() => RunSample("vulkan");

    [GodotBinaryFact(Driver = "d3d12")]
    public void D3D12() => RunSample("d3d12");

    [GodotBinaryFact(Driver = "opengl3")]
    public void OpenGl3() => RunSample("opengl3");

    static void RunSample(string renderingDriver)
    {
        var godot = Environment.GetEnvironmentVariable(GodotBinaryFactAttribute.EnvironmentVariable)!;
        var sampleDir = FindSampleDirectory();

        // Godot (run outside the editor) loads the project assembly from .godot/mono/temp/bin/Debug;
        // Tests.Godot.csproj's ProjectReference pins the sample build to Debug for that reason.
        // Check anyway, so a missing build fails here with a message instead of as a Godot crash.
        var assembly = Path.Combine(sampleDir, ".godot", "mono", "temp", "bin", "Debug", "Sample.Godot.dll");
        Assert.True(File.Exists(assembly),
            $"Sample assembly not found at {assembly}. Build samples/Sample.Godot first (any configuration of this test project builds it in Debug).");

        var screenshot = Path.Combine(Path.GetTempPath(), $"skiagamerendering-godot-{renderingDriver}-{Guid.NewGuid():N}.png");
        try
        {
            bool gpuValidation = renderingDriver == "vulkan" && Environment.GetEnvironmentVariable(GpuValidationVariable) == "1";
            var (exitCode, output) = RunGodot(godot, sampleDir, renderingDriver, screenshot, gpuValidation);
            Assert.True(exitCode == 0, $"Godot exited with {exitCode}.\n{output}");
            Assert.True(File.Exists(screenshot), $"Godot did not write {screenshot}.\n{output}");
            Assert.Contains($"SkiaGameRendering.Godot on {renderingDriver}", output);
            if (gpuValidation)
            {
                // Godot enables the layer silently and skips it silently when it is missing, so a
                // clean run proves nothing without this: the Vulkan loader's own log line, printed
                // because CI sets VK_LOADER_DEBUG=layer.
                Assert.Matches("Insert instance layer \"?VK_LAYER_KHRONOS_validation", output);
                Assert.DoesNotContain("VUID-", output);
                Assert.DoesNotContain("SYNC-HAZARD", output);
            }

            using var bitmap = SKBitmap.Decode(screenshot);
            Assert.NotNull(bitmap);
            int w = bitmap.Width, h = bitmap.Height;
            Assert.True(w > 100 && h > 100, $"Unexpectedly small screenshot: {w}x{h}");

            // Scene's grid: square cells half the shorter side wide, the circle in the first (top-left)
            // and the SVG drop in the second, each inset by a tenth of a cell.
            int cell = Math.Min(w, h) / 2;
            int radius = cell / 2 - cell / 10;
            AssertPixel(bitmap, cell / 2, cell / 2, Red, "circle center");
            AssertPixel(bitmap, cell / 2 - radius + 6, cell / 2, Red, "just inside the circle's left edge");
            // The same spot mirrored top-to-bottom: red there means the copy is flipped.
            AssertPixel(bitmap, cell / 2, h - 1 - cell / 2, CornflowerBlue, "circle center mirrored vertically (clear color)");
            AssertPixel(bitmap, 2, 2, CornflowerBlue, "top-left corner (clear color)");
            AssertPixel(bitmap, w - 3, 2, CornflowerBlue, "top-right corner (clear color)");
            AssertPixel(bitmap, 2, h - 3, CornflowerBlue, "bottom-left corner (clear color)");
            AssertPixel(bitmap, w - 3, h - 3, CornflowerBlue, "bottom-right corner (clear color)");

            // The drop is a blue gradient, close to the clear color, so check the red channel
            // rather than an exact value: about 50 at the drop's middle against the clear's 100.
            var drop = bitmap.GetPixel(cell + cell / 2, cell / 2);
            Assert.True(drop.Red < 80 && drop.Blue > 150, $"SVG drop center at ({cell + cell / 2},{cell / 2}): expected the drop's blue gradient, got {drop}.");
        }
        finally
        {
            if (File.Exists(screenshot))
                File.Delete(screenshot);
        }
    }

    /// <summary>
    /// Set to 1 to run the vulkan case under Godot's <c>--gpu-validation</c> (the Khronos validation
    /// layer, which must be installed) and fail on any <c>VUID-</c> or <c>SYNC-HAZARD</c> message.
    /// Also needs <c>VK_LOADER_DEBUG=layer</c> (the proof the layer loaded) and, for synchronization
    /// checks, <c>VK_LAYER_ENABLES=VK_VALIDATION_FEATURE_ENABLE_SYNCHRONIZATION_VALIDATION_EXT</c>;
    /// the <c>godot-linux</c> CI job sets all three.
    /// </summary>
    const string GpuValidationVariable = "SKIAGAMERENDERING_GODOT_GPU_VALIDATION";

    static (int exitCode, string output) RunGodot(string godot, string sampleDir, string renderingDriver, string screenshot, bool gpuValidation)
    {
        var startInfo = new ProcessStartInfo(godot)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(sampleDir);
        if (renderingDriver.StartsWith("opengl3", StringComparison.Ordinal))
        {
            // The Compatibility renderer is a rendering METHOD; its drivers are opengl3/opengl3_angle/opengl3_es.
            startInfo.ArgumentList.Add("--rendering-method");
            startInfo.ArgumentList.Add("gl_compatibility");
        }
        startInfo.ArgumentList.Add("--rendering-driver");
        startInfo.ArgumentList.Add(renderingDriver);
        if (gpuValidation)
            startInfo.ArgumentList.Add("--gpu-validation");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--screenshot");
        startInfo.ArgumentList.Add(screenshot);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        // Generous: CI runs on software rasterizers, with the validation layer on top for vulkan.
        if (!process.WaitForExit(TimeSpan.FromSeconds(300)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return (-1, "Timed out after 300s.\n" + stdout.Result + "\n" + stderr.Result);
        }
        return (process.ExitCode, stdout.Result + "\n" + stderr.Result);
    }

    static void AssertPixel(SKBitmap bitmap, int x, int y, SKColor expected, string what)
    {
        var actual = bitmap.GetPixel(x, y);
        const int tolerance = 2;
        bool close =
            Math.Abs(actual.Red - expected.Red) <= tolerance &&
            Math.Abs(actual.Green - expected.Green) <= tolerance &&
            Math.Abs(actual.Blue - expected.Blue) <= tolerance;
        Assert.True(close, $"Pixel at ({x},{y}), {what}: expected {expected}, got {actual}.");
    }

    static string FindSampleDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "Sample.Godot");
            if (File.Exists(Path.Combine(candidate, "project.godot")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate samples/Sample.Godot above " + AppContext.BaseDirectory);
    }
}
