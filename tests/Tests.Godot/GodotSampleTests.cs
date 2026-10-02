using System.Diagnostics;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

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
public class GodotSampleTests(ITestOutputHelper output)
{
    static readonly SKColor Red = new(255, 0, 0);
    static readonly SKColor CornflowerBlue = new(100, 149, 237);

    [GodotBinaryFact(Driver = "vulkan")]
    public void Vulkan() => RunSample("vulkan");

    [GodotBinaryFact(Driver = "d3d12")]
    public void D3D12() => RunSample("d3d12");

    [GodotBinaryFact(Driver = "metal")]
    public void Metal() => RunSample("metal");

    [GodotBinaryFact(Driver = "opengl3")]
    public void OpenGl3() => RunSample("opengl3");

    [GodotBinaryFact(Driver = "vulkan")]
    public void Vulkan_DrawingFramesGrowsNothing() => RunLeakCheck("vulkan");

    [GodotBinaryFact(Driver = "d3d12")]
    public void D3D12_DrawingFramesGrowsNothing() => RunLeakCheck("d3d12");

    [GodotBinaryFact(Driver = "metal")]
    public void Metal_DrawingFramesGrowsNothing() => RunLeakCheck("metal");

    [GodotBinaryFact(Driver = "opengl3")]
    public void OpenGl3_DrawingFramesGrowsNothing() => RunLeakCheck("opengl3");

    /// <summary>
    /// Runs the sample's <c>--leak-check</c> mode (see <c>samples/Sample.Godot/LeakCheck.cs</c>) with
    /// vsync off and a small window, so hundreds of frames fit in CI's software rasterizers, and fails
    /// if the second half of the run grew what the first half did not settle. Loose limits: Godot's
    /// own allocations move these numbers too, and a per-frame leak worth catching is a texture or
    /// command list, tens of kilobytes a frame.
    /// </summary>
    void RunLeakCheck(string renderingDriver)
    {
        var godot = Environment.GetEnvironmentVariable(GodotBinaryFactAttribute.EnvironmentVariable)!;
        var sampleDir = FindSampleDirectory();
        AssertSampleBuilt(sampleDir);

        const int frames = 400;
        var (exitCode, godotOutput) = RunGodot(godot, sampleDir, renderingDriver, gpuValidation: false,
            ["--disable-vsync", "--resolution", "256x256"], "--leak-check", frames.ToString());
        Assert.True(exitCode == 0, $"Godot exited with {exitCode}.\n{godotOutput}");

        var line = godotOutput.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Leak check:", StringComparison.Ordinal))
            ?? throw new Xunit.Sdk.XunitException("The sample printed no \"Leak check:\" line.\n" + godotOutput);
        output.WriteLine(line);
        var values = line["Leak check:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1].Split('/').Select(long.Parse).ToArray());

        long SecondHalf(string key) => values[key][2] - values[key][1];
        Assert.True(SecondHalf("privateBytes") < 48L * 1024 * 1024, "Private memory grew over the last frames. " + line);
        Assert.True(SecondHalf("videoMemory") < 16L * 1024 * 1024, "Godot's video memory grew over the last frames. " + line);
        Assert.True(SecondHalf("objects") <= 8, "Godot's object count grew over the last frames. " + line);
        Assert.True(SecondHalf("handles") < 64, "The process handle count grew over the last frames. " + line);
    }

    static void AssertSampleBuilt(string sampleDir)
    {
        // Godot (run outside the editor) loads the project assembly from .godot/mono/temp/bin/Debug;
        // Tests.Godot.csproj's ProjectReference pins the sample build to Debug for that reason.
        // Check anyway, so a missing build fails here with a message instead of as a Godot crash.
        var assembly = Path.Combine(sampleDir, ".godot", "mono", "temp", "bin", "Debug", "Sample.Godot.dll");
        Assert.True(File.Exists(assembly),
            $"Sample assembly not found at {assembly}. Build samples/Sample.Godot first (any configuration of this test project builds it in Debug).");
    }

    static void RunSample(string renderingDriver)
    {
        var godot = Environment.GetEnvironmentVariable(GodotBinaryFactAttribute.EnvironmentVariable)!;
        var sampleDir = FindSampleDirectory();
        AssertSampleBuilt(sampleDir);

        var screenshot = Path.Combine(Path.GetTempPath(), $"skiagamerendering-godot-{renderingDriver}-{Guid.NewGuid():N}.png");
        try
        {
            bool vulkanValidation = renderingDriver == "vulkan" && Environment.GetEnvironmentVariable(GpuValidationVariable) == "1";
            bool d3d12Validation = renderingDriver == "d3d12" && D3D12DebugLayerInstalled;
            var (exitCode, output) = RunGodot(godot, sampleDir, renderingDriver, vulkanValidation || d3d12Validation,
                [], "--screenshot", screenshot);
            Assert.True(exitCode == 0, $"Godot exited with {exitCode}.\n{output}");
            Assert.True(File.Exists(screenshot), $"Godot did not write {screenshot}.\n{output}");
            Assert.Contains($"SkiaGameRendering.Godot on {renderingDriver}", output);
            if (d3d12Validation)
            {
                // Printed by the sample from the debug layer's own queue (Godot does not print the
                // layer's messages). WARP renders the right pixels whatever the barriers say, so this
                // is what catches a handoff that leaves Godot's texture in the wrong layout.
                Assert.True(output.Contains("D3D12 debug layer: 0 error(s)"), "D3D12 debug layer errors:\n" + output);
            }
            if (vulkanValidation)
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

    /// <summary>
    /// Whether the D3D12 debug layer (<c>d3d12SDKLayers.dll</c>, the Windows "Graphics Tools" optional
    /// feature) is installed. When it is, the d3d12 case always runs under <c>--gpu-validation</c>,
    /// which on D3D12 is the plain debug layer: cheap, and it catches a barrier that mixes Godot's
    /// enhanced barriers with legacy ones. GPU-based validation is not an option: Godot's renderers
    /// fail to create their root signatures under it (E_OUTOFMEMORY) and never draw a frame.
    /// </summary>
    static bool D3D12DebugLayerInstalled =>
        OperatingSystem.IsWindows() && File.Exists(Path.Combine(Environment.SystemDirectory, "d3d12SDKLayers.dll"));

    /// <summary>
    /// Kills Godot past this much private memory rather than let it take the machine down: a Godot
    /// run left alone once grew to 22.5 GB (D3D12 GPU-based validation, which this never enables).
    /// </summary>
    const long MemoryCeiling = 4L * 1024 * 1024 * 1024;

    static (int exitCode, string output) RunGodot(string godot, string sampleDir, string renderingDriver, bool gpuValidation,
        string[] engineArgs, params string[] userArgs)
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
        foreach (var arg in engineArgs)
            startInfo.ArgumentList.Add(arg);
        startInfo.ArgumentList.Add("--");
        foreach (var arg in userArgs)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        // Generous: CI runs on software rasterizers, with the validation layer on top for vulkan.
        var deadline = Stopwatch.StartNew();
        while (!process.WaitForExit(TimeSpan.FromSeconds(1)))
        {
            string? killedBecause = null;
            if (deadline.Elapsed > TimeSpan.FromSeconds(300))
                killedBecause = "Timed out after 300s.";
            else
            {
                process.Refresh();
                var memory = process.PrivateMemorySize64 is > 0 and var bytes ? bytes : process.WorkingSet64;
                if (memory > MemoryCeiling)
                    killedBecause = $"Killed at {memory / (1024 * 1024)} MB of private memory.";
            }
            if (killedBecause != null)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return (-1, killedBecause + "\n" + stdout.Result + "\n" + stderr.Result);
            }
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
