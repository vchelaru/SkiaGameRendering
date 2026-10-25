using Xunit;

namespace Tests.CoreMetal;

/// <summary>The <see cref="MacOnlyFactAttribute"/> skip, for a theory.</summary>
public sealed class MacOnlyTheoryAttribute : TheoryAttribute
{
    public MacOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "Metal is macOS-only.";
    }
}
