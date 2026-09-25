using Xunit;

namespace Tests.Godot;

/// <summary>
/// Skips the decorated test at runtime unless the <c>GODOT_BIN</c> environment variable points at
/// a Godot 4.7+ .NET ("mono") build's executable, and unless <see cref="Driver"/> can run here.
/// GodotSharp types only function inside a running Godot process, so the Godot adapter cannot be
/// exercised in-process the way the other backends' tests exercise theirs; the only real test is
/// launching the engine against <c>samples/Sample.Godot</c>.
/// <para>
/// CI downloads the Godot .NET build and sets <c>GODOT_BIN</c> in <c>master.yml</c>'s Debug
/// <c>desktop-and-core</c> leg and in the <c>godot-linux</c> job. A driver listed in
/// <c>SKIAGAMERENDERING_GODOT_SKIP_DRIVERS</c> (comma-separated) is skipped with that reason, so a
/// runner that cannot host it reports a skip, not a pass.
/// </para>
/// </summary>
public sealed class GodotBinaryFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "GODOT_BIN";
    public const string SkipDriversVariable = "SKIAGAMERENDERING_GODOT_SKIP_DRIVERS";

    string _driver = "";

    public GodotBinaryFactAttribute()
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(path))
            Skip = $"{EnvironmentVariable} is not set - point it at a Godot 4.7+ .NET build's executable to run the Godot sample end to end.";
        else if (!File.Exists(path))
            Skip = $"{EnvironmentVariable} is set to '{path}', which does not exist.";
    }

    /// <summary>The <c>--rendering-driver</c> the test runs; decides where it can run.</summary>
    public string Driver
    {
        get => _driver;
        set
        {
            _driver = value;
            if (Skip != null)
                return;
            if (OperatingSystem.IsMacOS())
                Skip = "The Godot adapter does not support macOS.";
            else if (value == "d3d12" && !OperatingSystem.IsWindows())
                Skip = "Godot offers d3d12 only on Windows.";
            else if ((Environment.GetEnvironmentVariable(SkipDriversVariable) ?? "")
                     .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                     .Contains(value))
                Skip = $"'{value}' is listed in {SkipDriversVariable}.";
        }
    }
}
